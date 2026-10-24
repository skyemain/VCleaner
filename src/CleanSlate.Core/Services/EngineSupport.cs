using System.Globalization;
using System.Text;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Services;

public sealed class EngineBusyException : InvalidOperationException
{
    public EngineBusyException() : base("Another scan, cleanup or registry repair is already running.") { }
}

/// <summary>Ensures only one scan/clean/repair operation runs at a time.</summary>
public sealed class RunGate
{
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public IDisposable Enter()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new EngineBusyException();
        }

        return new Lease(this);
    }

    private sealed class Lease : IDisposable
    {
        private RunGate? _gate;
        public Lease(RunGate gate) => _gate = gate;

        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            if (gate is not null)
            {
                Volatile.Write(ref gate._busy, 0);
            }
        }
    }
}

public static class ByteFormatter
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    /// <summary>Formats a byte count. Zero is shown as "0 MB" so the idle UI matches the design.</summary>
    public static string Format(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 MB";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}

/// <summary>Thread-safe progress aggregation with throttled publishing.</summary>
internal sealed class ProgressAggregator
{
    private readonly IProgress<EngineProgress>? _sink;
    private readonly object _gate = new();
    private readonly Dictionary<CleanerGroup, GroupProgress> _groups = new();
    private readonly TimeSpan _throttle;
    private long _lastPublishTicks;

    private EnginePhase _phase = EnginePhase.Idle;
    private string _operation = string.Empty;
    private long _items;
    private long _estimated;
    private long _recovered;
    private int _processed;
    private int _skipped;
    private int _errors;
    private int _categoriesDone;

    public ProgressAggregator(IProgress<EngineProgress>? sink, TimeSpan? throttle = null)
    {
        _sink = sink;
        _throttle = throttle ?? TimeSpan.FromMilliseconds(100);
    }

    public int ActionsTotal { get; set; }
    public int CategoriesTotal { get; set; }

    public void SetPhase(EnginePhase phase, string operation)
    {
        lock (_gate)
        {
            _phase = phase;
            _operation = operation;
        }

        Publish(force: true);
    }

    public void SetGroup(CleanerGroup group, GroupState state, string? message = null)
    {
        lock (_gate)
        {
            _groups.TryGetValue(group, out var existing);
            _groups[group] = new GroupProgress(state, existing?.Items ?? 0, existing?.EstimatedBytes ?? 0, existing?.RecoveredBytes ?? 0, message ?? existing?.Message);
        }

        Publish(force: true);
    }

    public void ModuleFinished()
    {
        lock (_gate)
        {
            _categoriesDone++;
        }

        Publish(force: true);
    }

    public IModuleReporter ForModule(CleanerGroup group) => new ModuleReporter(this, group);

    public void Publish(bool force)
    {
        if (_sink is null)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (!force)
        {
            var last = Interlocked.Read(ref _lastPublishTicks);
            if (now - last < _throttle.TotalMilliseconds)
            {
                return;
            }
        }

        Interlocked.Exchange(ref _lastPublishTicks, now);
        _sink.Report(Snapshot());
    }

    public EngineProgress Snapshot()
    {
        lock (_gate)
        {
            return new EngineProgress(
                _phase, _operation, _items, _estimated, _processed, ActionsTotal, _categoriesDone, CategoriesTotal,
                _recovered, _skipped, _errors, new Dictionary<CleanerGroup, GroupProgress>(_groups));
        }
    }

    private sealed class ModuleReporter : IModuleReporter
    {
        private readonly ProgressAggregator _owner;
        private readonly CleanerGroup _group;

        public ModuleReporter(ProgressAggregator owner, CleanerGroup group)
        {
            _owner = owner;
            _group = group;
        }

        public void Operation(string text)
        {
            lock (_owner._gate)
            {
                _owner._operation = text;
            }

            _owner.Publish(force: false);
        }

        public void Scanned(long items, long bytes)
        {
            lock (_owner._gate)
            {
                _owner._items += items;
                _owner._estimated += bytes;
                _owner._groups.TryGetValue(_group, out var g);
                g ??= new GroupProgress(GroupState.Scanning, 0, 0, 0, null);
                _owner._groups[_group] = g with { Items = g.Items + items, EstimatedBytes = g.EstimatedBytes + bytes };
            }

            _owner.Publish(force: false);
        }

        public void Processed(int actions, long bytesRecovered, int skipped, int errors)
        {
            lock (_owner._gate)
            {
                _owner._processed += actions;
                _owner._recovered += bytesRecovered;
                _owner._skipped += skipped;
                _owner._errors += errors;
                _owner._groups.TryGetValue(_group, out var g);
                g ??= new GroupProgress(GroupState.Cleaning, 0, 0, 0, null);
                _owner._groups[_group] = g with { RecoveredBytes = g.RecoveredBytes + bytesRecovered };
            }

            _owner.Publish(force: false);
        }
    }
}

/// <summary>Turns scan results into the set of actions that are allowed to run.</summary>
public static class PlanBuilder
{
    public static CleanPlan Build(ScanReport report, PlanMode mode)
    {
        var allowed = new List<Finding>();
        var rejected = new List<(Finding, string)>();

        foreach (var finding in report.AllFindings)
        {
            if (!finding.IsSelected && mode == PlanMode.Reviewed)
            {
                continue;
            }

            var reason = Check(finding, mode);
            if (reason is null)
            {
                allowed.Add(finding);
            }
            else if (finding.Action != ActionType.None && finding.IsEligible)
            {
                rejected.Add((finding, reason));
            }
        }

        return new CleanPlan { Mode = mode, Findings = allowed, Rejected = rejected };
    }

    /// <summary>Returns null when a finding may execute in the given mode, otherwise the reason it may not.</summary>
    public static string? Check(Finding finding, PlanMode mode)
    {
        if (finding.Action is ActionType.None)
        {
            return "Informational";
        }

        if (finding.Action == ActionType.RepairRegistryEntry)
        {
            return "Registry repairs require explicit review and backups";
        }

        if (!finding.IsEligible)
        {
            return finding.SkipReason ?? "Not eligible";
        }

        if (mode == PlanMode.Automatic)
        {
            if (!finding.AutoCleanAllowed)
            {
                return "Not classified as safe for automatic cleaning";
            }

            if (finding.ConfirmationRequired)
            {
                return "Requires confirmation";
            }
        }
        else if (!finding.IsSelected)
        {
            return "Not selected";
        }

        return null;
    }

    /// <summary>
    /// True when the user's settings enable something that must be confirmed first. One-click then opens the
    /// review screen instead of silently running permanent or privacy-sensitive operations.
    /// </summary>
    public static bool RequiresReview(ScanReport report) =>
        report.AllFindings.Any(f => f.IsEligible && f.IsSelected && f.ConfirmationRequired &&
                                    f.Action is not (ActionType.None or ActionType.RepairRegistryEntry));
}

public static class SessionRecorder
{
    public static SessionRecord Create(ScanReport scan, CleanReport? clean, PlanMode mode, int registryRepaired = 0, IEnumerable<SessionRegistryChange>? registryChanges = null)
    {
        var record = new SessionRecord
        {
            ScanTimestamp = scan.StartedAt,
            CleanupTimestamp = clean?.CompletedAt,
            Mode = mode == PlanMode.Automatic ? "Automatic" : "Reviewed",
            WasElevated = scan.IsElevated,
            WasCanceled = scan.WasCanceled || (clean?.WasCanceled ?? false),
            DurationSeconds = ((clean?.CompletedAt ?? scan.CompletedAt) - scan.StartedAt).TotalSeconds,
            TotalEstimatedBytes = scan.EligibleBytes,
            TotalRecoveredBytes = clean?.BytesRecovered ?? 0,
            ItemsCleaned = clean?.ItemsCleaned ?? 0,
            ItemsSkipped = (clean?.ItemsSkipped ?? 0) + scan.Modules.Sum(m => m.SkippedItemCount),
            Errors = (clean?.Errors ?? 0) + scan.Modules.Sum(m => m.Errors.Count),
            RegistryIssuesDetected = scan.AllFindings.Count(f => f.Action == ActionType.RepairRegistryEntry),
            RegistryIssuesRepaired = registryRepaired
        };

        var cleanedByCategory = clean?.Modules.SelectMany(m => m.Categories).ToDictionary(c => (c.ModuleId, c.Category))
                                ?? new Dictionary<(string, string), CategoryResult>();

        foreach (var module in scan.Modules)
        {
            if (module.Findings.Count == 0 && module.Status != ModuleScanStatus.Completed)
            {
                record.Categories.Add(new SessionCategoryRecord
                {
                    Module = module.ModuleName,
                    Category = module.ModuleName,
                    Note = $"{module.Status}: {module.Message}"
                });
                continue;
            }

            foreach (var group in module.Findings.GroupBy(f => f.Category))
            {
                var actionable = group.Where(f => f.Action != ActionType.None && f.IsEligible).ToList();
                cleanedByCategory.TryGetValue((module.ModuleId, group.Key), out var result);
                record.Categories.Add(new SessionCategoryRecord
                {
                    Module = module.ModuleName,
                    Category = group.Key,
                    ItemsFound = actionable.Count,
                    EstimatedBytes = actionable.Sum(f => f.EstimatedBytes),
                    ItemsCleaned = result?.Cleaned ?? 0,
                    ItemsSkipped = result?.Skipped ?? 0,
                    Errors = result?.Errors ?? 0,
                    RecoveredBytes = result?.RecoveredBytes ?? 0,
                    Note = actionable.Count == 0 ? group.FirstOrDefault(f => !f.IsEligible)?.SkipReason : null
                });
            }
        }

        if (registryChanges is not null)
        {
            record.RegistryChanges.AddRange(registryChanges);
        }

        return record;
    }
}

public static class ReportExporter
{
    public static string ToText(IEnumerable<SessionRecord> sessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CleanSlate report");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine("Space figures are logical file sizes of items that were successfully deleted.");
        sb.AppendLine();

        foreach (var s in sessions)
        {
            sb.AppendLine($"Session {s.Id[..Math.Min(8, s.Id.Length)]}  ({s.Mode}{(s.WasCanceled ? ", canceled" : string.Empty)}{(s.WasElevated ? ", administrator" : string.Empty)})");
            sb.AppendLine($"  Scan:    {s.ScanTimestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"  Cleanup: {(s.CleanupTimestamp is { } done ? done.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "not performed")}");
            sb.AppendLine($"  Duration: {s.DurationSeconds:0.0} s");
            sb.AppendLine($"  Estimated: {ByteFormatter.Format(s.TotalEstimatedBytes)}   Recovered: {ByteFormatter.Format(s.TotalRecoveredBytes)}");
            sb.AppendLine($"  Items cleaned: {s.ItemsCleaned}   skipped: {s.ItemsSkipped}   errors: {s.Errors}");
            sb.AppendLine($"  Registry issues detected: {s.RegistryIssuesDetected}   repaired: {s.RegistryIssuesRepaired}");
            foreach (var c in s.Categories)
            {
                sb.AppendLine($"    - {c.Module} / {c.Category}: found {c.ItemsFound}, cleaned {c.ItemsCleaned}, skipped {c.ItemsSkipped}, errors {c.Errors}, estimated {ByteFormatter.Format(c.EstimatedBytes)}, recovered {ByteFormatter.Format(c.RecoveredBytes)}{(c.Note is null ? string.Empty : "  [" + c.Note + "]")}");
            }

            foreach (var r in s.RegistryChanges)
            {
                sb.AppendLine($"    * Registry [{r.Rule}] {r.Location}: {r.Status}{(r.BackupId is null ? string.Empty : "  (backup " + r.BackupId + ")")}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
