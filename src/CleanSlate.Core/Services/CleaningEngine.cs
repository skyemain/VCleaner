using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Services;

public interface ICleaningEngine
{
    bool IsBusy { get; }

    /// <summary>Read-only scan of every enabled module. Never modifies the system.</summary>
    Task<ScanReport> ScanAsync(CleanSlateSettings settings, IProgress<EngineProgress>? progress, CancellationToken cancellationToken);

    /// <summary>Executes an approved plan. Each finding is revalidated by its module immediately before it runs.</summary>
    Task<CleanReport> CleanAsync(CleanPlan plan, CleanSlateSettings settings, IProgress<EngineProgress>? progress, CancellationToken cancellationToken);
}

public sealed class CleaningEngine : ICleaningEngine
{
    private const int MaxParallelModules = 3;

    private readonly IReadOnlyList<ICleanerModule> _modules;
    private readonly IPrivilegeInfo _privileges;
    private readonly IAppLogger _log;
    private readonly RunGate _gate;
    private readonly TimeProvider _time;

    public CleaningEngine(IEnumerable<ICleanerModule> modules, IPrivilegeInfo privileges, IAppLogger log, RunGate gate, TimeProvider? time = null)
    {
        _modules = modules.OrderBy(m => m.Group).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
        _privileges = privileges;
        _log = log;
        _gate = gate;
        _time = time ?? TimeProvider.System;
    }

    public bool IsBusy => _gate.IsBusy;

    public IReadOnlyList<ICleanerModule> Modules => _modules;

    public async Task<ScanReport> ScanAsync(CleanSlateSettings settings, IProgress<EngineProgress>? progress, CancellationToken cancellationToken)
    {
        using var lease = _gate.Enter();
        var snapshot = settings.Clone();
        var started = _time.GetUtcNow();
        var elevated = _privileges.IsElevated;
        var aggregator = new ProgressAggregator(progress);
        var claims = new ClaimSet();
        var exclusions = ExclusionPolicy.FromSettings(snapshot);

        var active = _modules.Where(m => m.IsEnabled(snapshot)).ToList();
        var disabled = _modules.Where(m => !m.IsEnabled(snapshot)).ToList();
        aggregator.CategoriesTotal = active.Count;
        aggregator.SetPhase(EnginePhase.Scanning, "Preparing scan...");

        foreach (var group in active.Select(m => m.Group).Distinct())
        {
            aggregator.SetGroup(group, GroupState.Waiting);
        }

        _log.Info($"Scan started ({active.Count} modules, elevated={elevated}).");

        var results = new ModuleScanResult?[active.Count];
        using var semaphore = new SemaphoreSlim(MaxParallelModules);
        var canceled = false;

        var tasks = active.Select(async (module, index) =>
        {
            await semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                results[index] = await ScanModuleAsync(module, snapshot, elevated, claims, exclusions, aggregator, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        canceled = cancellationToken.IsCancellationRequested;

        var modules = results.Where(r => r is not null).Select(r => r!).ToList();
        foreach (var module in disabled)
        {
            modules.Add(ModuleScanResult.Empty(module.ModuleId, module.Name, module.Group, ModuleScanStatus.Disabled, "Disabled in settings"));
        }

        FinalizeGroups(modules, aggregator, canceled);
        aggregator.SetPhase(canceled ? EnginePhase.Canceled : EnginePhase.Completed, canceled ? "Scan canceled" : "Scan complete");

        var report = new ScanReport
        {
            StartedAt = started,
            CompletedAt = _time.GetUtcNow(),
            WasCanceled = canceled,
            IsElevated = elevated,
            Modules = modules.OrderBy(m => m.Group).ThenBy(m => m.ModuleName, StringComparer.Ordinal).ToList()
        };

        _log.Info($"Scan finished: {report.TotalItemsScanned} items scanned, {ByteFormatter.Format(report.EligibleBytes)} recoverable, canceled={canceled}.");
        return report;
    }

    private async Task<ModuleScanResult> ScanModuleAsync(
        ICleanerModule module, CleanSlateSettings settings, bool elevated, ClaimSet claims,
        ExclusionPolicy exclusions, ProgressAggregator aggregator, CancellationToken ct)
    {
        try
        {
            if (ct.IsCancellationRequested)
            {
                return ModuleScanResult.Empty(module.ModuleId, module.Name, module.Group, ModuleScanStatus.Canceled, "Canceled");
            }

            if (module.RequiresElevation && !elevated)
            {
                aggregator.ModuleFinished();
                return ModuleScanResult.Empty(module.ModuleId, module.Name, module.Group, ModuleScanStatus.Skipped,
                    "Skipped: requires administrator");
            }

            aggregator.SetGroup(module.Group, GroupState.Scanning);
            aggregator.ForModule(module.Group).Operation($"Scanning {module.Name}...");

            var context = new ScanContext
            {
                Settings = settings,
                Reporter = aggregator.ForModule(module.Group),
                Claims = claims,
                Exclusions = exclusions,
                IsElevated = elevated,
                NowUtc = _time.GetUtcNow().UtcDateTime
            };

            var result = await module.ScanAsync(context, ct).ConfigureAwait(false);
            aggregator.ModuleFinished();
            return result;
        }
        catch (OperationCanceledException)
        {
            return ModuleScanResult.Empty(module.ModuleId, module.Name, module.Group, ModuleScanStatus.Canceled, "Canceled");
        }
        catch (Exception ex)
        {
            _log.Error($"Module '{module.ModuleId}' failed during scan.", ex);
            aggregator.ModuleFinished();
            return new ModuleScanResult
            {
                ModuleId = module.ModuleId,
                ModuleName = module.Name,
                Group = module.Group,
                Status = ModuleScanStatus.Failed,
                Message = "Scan failed: " + ex.Message,
                Errors = { ex.Message }
            };
        }
    }

    private static void FinalizeGroups(IReadOnlyList<ModuleScanResult> modules, ProgressAggregator aggregator, bool canceled)
    {
        foreach (var group in modules.GroupBy(m => m.Group))
        {
            var list = group.ToList();
            GroupState state;
            string? message = null;

            if (list.All(m => m.Status == ModuleScanStatus.Disabled))
            {
                state = GroupState.Disabled;
                message = "Disabled in settings";
            }
            else if (canceled && list.Any(m => m.Status == ModuleScanStatus.Canceled))
            {
                state = GroupState.Canceled;
                message = "Canceled";
            }
            else if (list.Where(m => m.Status != ModuleScanStatus.Disabled).All(m => m.Status == ModuleScanStatus.Unavailable))
            {
                state = GroupState.Unavailable;
                message = list.First(m => m.Status == ModuleScanStatus.Unavailable).Message;
            }
            else if (list.Where(m => m.Status != ModuleScanStatus.Disabled).All(m => m.Status == ModuleScanStatus.Skipped))
            {
                state = GroupState.Skipped;
                message = list.First(m => m.Status == ModuleScanStatus.Skipped).Message;
            }
            else if (list.All(m => m.Status is ModuleScanStatus.Failed or ModuleScanStatus.Disabled))
            {
                state = GroupState.Failed;
                message = list.First(m => m.Status == ModuleScanStatus.Failed).Message;
            }
            else
            {
                state = GroupState.Scanned;
            }

            aggregator.SetGroup(group.Key, state, message);
        }
    }

    public async Task<CleanReport> CleanAsync(CleanPlan plan, CleanSlateSettings settings, IProgress<EngineProgress>? progress, CancellationToken cancellationToken)
    {
        using var lease = _gate.Enter();
        var snapshot = settings.Clone();
        var started = _time.GetUtcNow();
        var elevated = _privileges.IsElevated;
        var exclusions = ExclusionPolicy.FromSettings(snapshot);

        var executable = plan.Findings.Where(f => PlanBuilder.Check(f, plan.Mode) is null).ToList();
        var byModule = executable.GroupBy(f => f.ModuleId).ToList();

        var aggregator = new ProgressAggregator(progress)
        {
            ActionsTotal = executable.Count,
            CategoriesTotal = byModule.Count
        };

        aggregator.SetPhase(EnginePhase.Cleaning, "Preparing cleanup...");
        foreach (var group in executable.Select(f => f.Group).Distinct())
        {
            aggregator.SetGroup(group, GroupState.Waiting);
        }

        _log.Info($"Cleanup started: {executable.Count} actions in {byModule.Count} modules, mode={plan.Mode}.");

        var results = new List<ModuleCleanResult>();
        using var semaphore = new SemaphoreSlim(MaxParallelModules);

        var tasks = byModule.Select(async moduleGroup =>
        {
            var module = _modules.FirstOrDefault(m => m.ModuleId == moduleGroup.Key);
            if (module is null)
            {
                return;
            }

            await semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var result = await CleanModuleAsync(module, moduleGroup.ToList(), snapshot, elevated, exclusions, aggregator, cancellationToken).ConfigureAwait(false);
                lock (results)
                {
                    results.Add(result);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        var canceled = cancellationToken.IsCancellationRequested;

        foreach (var group in results.GroupBy(r => r.Group))
        {
            aggregator.SetGroup(group.Key, canceled ? GroupState.Canceled : GroupState.Cleaned);
        }

        aggregator.SetPhase(canceled ? EnginePhase.Canceled : EnginePhase.Completed, canceled ? "Cleanup canceled" : "Cleanup complete");

        var report = new CleanReport
        {
            StartedAt = started,
            CompletedAt = _time.GetUtcNow(),
            WasCanceled = canceled,
            Modules = results.OrderBy(r => r.Group).ThenBy(r => r.ModuleName, StringComparer.Ordinal).ToList()
        };

        _log.Info($"Cleanup finished: {report.ItemsCleaned} cleaned, {report.ItemsSkipped} skipped, {report.Errors} errors, {ByteFormatter.Format(report.BytesRecovered)} recovered, canceled={canceled}.");
        return report;
    }

    private async Task<ModuleCleanResult> CleanModuleAsync(
        ICleanerModule module, IReadOnlyList<Finding> findings, CleanSlateSettings settings, bool elevated,
        ExclusionPolicy exclusions, ProgressAggregator aggregator, CancellationToken ct)
    {
        try
        {
            if (module.RequiresElevation && !elevated)
            {
                aggregator.ForModule(module.Group).Processed(findings.Count, 0, findings.Count, 0);
                return new ModuleCleanResult
                {
                    ModuleId = module.ModuleId,
                    ModuleName = module.Name,
                    Group = module.Group,
                    ItemsSkipped = findings.Count,
                    Message = "Skipped: requires administrator"
                };
            }

            aggregator.SetGroup(module.Group, GroupState.Cleaning);
            aggregator.ForModule(module.Group).Operation($"Cleaning {module.Name}...");

            var context = new CleanContext
            {
                Settings = settings,
                Reporter = aggregator.ForModule(module.Group),
                Exclusions = exclusions,
                IsElevated = elevated,
                NowUtc = _time.GetUtcNow().UtcDateTime
            };

            var result = await module.CleanAsync(context, findings, ct).ConfigureAwait(false);
            aggregator.ModuleFinished();
            return result;
        }
        catch (OperationCanceledException)
        {
            return new ModuleCleanResult { ModuleId = module.ModuleId, ModuleName = module.Name, Group = module.Group, WasCanceled = true, Message = "Canceled" };
        }
        catch (Exception ex)
        {
            _log.Error($"Module '{module.ModuleId}' failed during cleanup.", ex);
            aggregator.ModuleFinished();
            return new ModuleCleanResult
            {
                ModuleId = module.ModuleId,
                ModuleName = module.Name,
                Group = module.Group,
                Errors = 1,
                Message = "Cleanup failed: " + ex.Message
            };
        }
    }
}
