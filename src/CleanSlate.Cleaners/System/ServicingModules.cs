using System.IO.Enumeration;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;

namespace CleanSlate.Cleaners.SystemJunk;

/// <summary>
/// Base for modules that delegate to a fixed, supported Windows servicing command instead of deleting files.
/// Only a single, hard-coded command line is ever executed; no user input reaches the process runner.
/// </summary>
public abstract class ServicingModuleBase : ICleanerModule
{
    private static long _nextId = 2_000_000_000;

    protected ServicingModuleBase(SystemPaths paths, IFileProbe probe, IProcessRunner runner, IAppLogger log)
    {
        Paths = paths;
        Probe = probe;
        Runner = runner;
        Log = log;
    }

    protected SystemPaths Paths { get; }
    protected IFileProbe Probe { get; }
    protected IProcessRunner Runner { get; }
    protected IAppLogger Log { get; }

    public abstract string ModuleId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public CleanerGroup Group => CleanerGroup.SystemJunk;
    public virtual RiskLevel RiskLevel => RiskLevel.Medium;
    public bool RequiresElevation => true;
    public abstract bool SupportsPreview { get; }
    public abstract bool IsEnabled(CleanSlateSettings settings);

    protected abstract string CategoryName { get; }
    protected abstract string? FindUnavailableReason();
    protected abstract long MeasureBytes(CancellationToken ct);
    protected abstract string ExecutablePath { get; }
    protected abstract string Arguments { get; }
    protected virtual TimeSpan Timeout => TimeSpan.FromMinutes(45);
    protected abstract string ActionDescription { get; }

    /// <summary>Exit codes that count as success (3010 = success, reboot required).</summary>
    protected virtual bool IsSuccess(int exitCode) => exitCode is 0 or 3010;

    public Task<ModuleScanResult> ScanAsync(ScanContext context, CancellationToken cancellationToken)
    {
        if (!context.IsElevated)
        {
            return Task.FromResult(ModuleScanResult.Empty(ModuleId, Name, Group, ModuleScanStatus.Skipped, "Skipped: requires administrator"));
        }

        var unavailable = FindUnavailableReason();
        if (unavailable is not null)
        {
            return Task.FromResult(ModuleScanResult.Empty(ModuleId, Name, Group, ModuleScanStatus.Unavailable, unavailable));
        }

        long bytes;
        try
        {
            bytes = SupportsPreview ? MeasureBytes(cancellationToken) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = 0;
        }

        var result = new ModuleScanResult { ModuleId = ModuleId, ModuleName = Name, Group = Group };
        if (SupportsPreview && bytes == 0)
        {
            result = new ModuleScanResult { ModuleId = ModuleId, ModuleName = Name, Group = Group, Message = "Nothing to clean" };
            return Task.FromResult(result);
        }

        context.Reporter.Scanned(1, bytes);
        result.Findings.Add(new Finding
        {
            Id = Interlocked.Increment(ref _nextId),
            ModuleId = ModuleId,
            Group = Group,
            Category = CategoryName,
            Location = "Windows servicing",
            Description = ActionDescription,
            EstimatedBytes = bytes,
            Risk = RiskLevel,
            Action = ActionType.RunServicing,
            IsSelected = true,
            IsEligible = true,
            AutoCleanAllowed = false,
            ConfirmationRequired = true,
            IsPermanent = true
        });
        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(Finding finding, CleanContext context, CancellationToken cancellationToken)
    {
        if (finding.ModuleId != ModuleId || finding.Action != ActionType.RunServicing)
        {
            return Task.FromResult(ValidationResult.Invalid("Finding does not belong to this module."));
        }

        if (!context.IsElevated)
        {
            return Task.FromResult(ValidationResult.Invalid("Requires administrator."));
        }

        if (!IsEnabled(context.Settings))
        {
            return Task.FromResult(ValidationResult.Invalid("Disabled in settings."));
        }

        var unavailable = FindUnavailableReason();
        return Task.FromResult(unavailable is null ? ValidationResult.Valid : ValidationResult.Invalid(unavailable));
    }

    public async Task<ModuleCleanResult> CleanAsync(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        var finding = findings.FirstOrDefault(f => f.ModuleId == ModuleId && f.Action == ActionType.RunServicing);
        if (finding is null)
        {
            return new ModuleCleanResult { ModuleId = ModuleId, ModuleName = Name, Group = Group };
        }

        var validation = await ValidateAsync(finding, context, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            context.Reporter.Processed(1, 0, 1, 0);
            return Build(finding, 0, 1, 0, 0, validation.Reason);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            long before = SupportsPreview ? MeasureBytes(CancellationToken.None) : 0;
            Log.Audit($"SERVICING run: {Path.GetFileName(ExecutablePath)} {Arguments}");
            var result = await Runner.RunAsync(ExecutablePath, Arguments, Timeout, cancellationToken).ConfigureAwait(false);

            if (result.TimedOut)
            {
                context.Reporter.Processed(1, 0, 0, 1);
                return Build(finding, 0, 0, 1, 0, "Timed out while waiting for Windows servicing.");
            }

            if (!IsSuccess(result.ExitCode))
            {
                Log.Warning($"Servicing command for {ModuleId} exited with code {result.ExitCode}.");
                context.Reporter.Processed(1, 0, 0, 1);
                return Build(finding, 0, 0, 1, 0, $"Windows reported exit code 0x{result.ExitCode:X8}.");
            }

            var recovered = SupportsPreview ? Math.Max(0, before - MeasureBytes(CancellationToken.None)) : 0;
            context.Reporter.Processed(1, recovered, 0, 0);
            return Build(finding, 1, 0, 0, recovered,
                SupportsPreview ? null : "Completed. Recovered space is not measured for this operation.");
        }
        catch (OperationCanceledException)
        {
            Log.Warning($"{ModuleId}: canceled while the servicing command was running.");
            return new ModuleCleanResult { ModuleId = ModuleId, ModuleName = Name, Group = Group, WasCanceled = true, Message = "Canceled" };
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"{ModuleId} failed.", ex);
            context.Reporter.Processed(1, 0, 0, 1);
            return Build(finding, 0, 0, 1, 0, ex.Message);
        }
    }

    private ModuleCleanResult Build(Finding finding, int cleaned, int skipped, int errors, long recovered, string? message) => new()
    {
        ModuleId = ModuleId,
        ModuleName = Name,
        Group = Group,
        ItemsCleaned = cleaned,
        ItemsSkipped = skipped,
        Errors = errors,
        BytesRecovered = recovered,
        Message = message,
        Categories = new[] { new CategoryResult(ModuleId, Group, CategoryName, cleaned, skipped, errors, finding.EstimatedBytes, recovered) },
        Outcomes = errors + skipped > 0 && message is not null
            ? new[] { new ActionOutcome(errors > 0 ? OutcomeKind.Error : OutcomeKind.Skipped, ModuleId, CategoryName, finding.Location, message, 0) }
            : Array.Empty<ActionOutcome>()
    };

    /// <summary>Sums file sizes below a folder without following reparse points. Read-only.</summary>
    protected static long MeasureDirectory(string path, CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            try
            {
                var entries = new FileSystemEnumerable<(string Path, bool IsDir, bool IsLink, long Length)>(
                    dir,
                    (ref FileSystemEntry e) => (e.ToFullPath(), e.IsDirectory, (e.Attributes & FileAttributes.ReparsePoint) != 0, e.IsDirectory ? 0 : e.Length),
                    options);

                foreach (var entry in entries)
                {
                    if (entry.IsLink)
                    {
                        continue;
                    }

                    if (entry.IsDir)
                    {
                        pending.Push(entry.Path);
                    }
                    else
                    {
                        total += entry.Length;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable folder: not counted.
            }
        }

        return total;
    }
}

/// <summary>Windows Update cleanup through DISM component-store cleanup (the supported servicing mechanism).</summary>
public sealed class WindowsUpdateCleanupModule : ServicingModuleBase
{
    public WindowsUpdateCleanupModule(SystemPaths paths, IFileProbe probe, IProcessRunner runner, IAppLogger log)
        : base(paths, probe, runner, log)
    {
    }

    public override string ModuleId => "system.windows-update-cleanup";
    public override string Name => "Windows Update cleanup";
    public override string Description => "Runs the supported DISM component-store cleanup to remove superseded Windows components. Requires administrator and can take several minutes.";
    public override bool SupportsPreview => false;
    protected override string CategoryName => "Windows Update cleanup";
    protected override string ExecutablePath => Path.Combine(Paths.SystemDirectory, "dism.exe");
    protected override string Arguments => "/Online /Cleanup-Image /StartComponentCleanup";
    protected override string ActionDescription => "Run Windows component cleanup (DISM /StartComponentCleanup). Space cannot be estimated in advance; superseded updates can no longer be uninstalled afterwards.";

    public override bool IsEnabled(CleanSlateSettings settings) => settings.CleanWindowsUpdateComponents;

    protected override string? FindUnavailableReason() =>
        Probe.FileExists(ExecutablePath) ? null : "Unavailable: the Windows servicing tool (DISM) was not found.";

    protected override long MeasureBytes(CancellationToken ct) => 0;
}

/// <summary>Delivery Optimization cache cleanup through the supported Delete-DeliveryOptimizationCache cmdlet.</summary>
public sealed class DeliveryOptimizationModule : ServicingModuleBase
{
    public DeliveryOptimizationModule(SystemPaths paths, IFileProbe probe, IProcessRunner runner, IAppLogger log)
        : base(paths, probe, runner, log)
    {
    }

    public override string ModuleId => "system.delivery-optimization";
    public override string Name => "Delivery Optimization cache";
    public override string Description => "Clears the Windows Update peer-to-peer download cache using the supported Windows cmdlet. Requires administrator.";
    public override bool SupportsPreview => true;
    public override RiskLevel RiskLevel => RiskLevel.Low;
    protected override string CategoryName => "Delivery Optimization cache";

    protected override string ExecutablePath => Path.Combine(Paths.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
    protected override string Arguments => "-NoProfile -NonInteractive -Command \"Delete-DeliveryOptimizationCache -Force\"";
    protected override string ActionDescription => "Clear the Delivery Optimization cache (Delete-DeliveryOptimizationCache)";

    private string CacheDirectory => Path.Combine(Paths.WindowsDirectory, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache");

    public override bool IsEnabled(CleanSlateSettings settings) => settings.CleanDeliveryOptimization;

    protected override string? FindUnavailableReason() =>
        Probe.FileExists(ExecutablePath) ? null : "Unavailable: Windows PowerShell was not found.";

    protected override long MeasureBytes(CancellationToken ct) => MeasureDirectory(CacheDirectory, ct);
}
