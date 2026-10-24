using CleanSlate.Core.Models;
using CleanSlate.Core.Services;

namespace CleanSlate.Presentation.ViewModels;

public enum RowKind
{
    Neutral,
    Active,
    Success,
    Warning,
    Error
}

public static class GroupStatusFormatter
{
    public static string Title(CleanerGroup group) => group switch
    {
        CleanerGroup.SystemJunk => "SYSTEM JUNK",
        CleanerGroup.Chrome => "CHROME",
        CleanerGroup.Edge => "EDGE",
        CleanerGroup.Registry => "REGISTRY",
        CleanerGroup.Privacy => "PRIVACY",
        _ => group.ToString().ToUpperInvariant()
    };

    public static (string Text, RowKind Kind) FromProgress(CleanerGroup group, GroupProgress progress)
    {
        return progress.State switch
        {
            GroupState.Waiting => ("Waiting...", RowKind.Neutral),
            GroupState.Scanning => ($"Scanning... {progress.Items:N0} items", RowKind.Active),
            GroupState.Cleaning => (progress.RecoveredBytes > 0 ? $"Cleaning... {ByteFormatter.Format(progress.RecoveredBytes)} so far" : "Cleaning...", RowKind.Active),
            GroupState.Disabled => ("Disabled in settings", RowKind.Neutral),
            GroupState.Unavailable => (progress.Message ?? "Not available", RowKind.Neutral),
            GroupState.Skipped => (progress.Message ?? "Skipped", RowKind.Warning),
            GroupState.Failed => (progress.Message ?? "Failed", RowKind.Error),
            GroupState.Canceled => ("Canceled", RowKind.Warning),
            GroupState.Cleaned => ("Cleaned", RowKind.Success),
            _ => ("Scanned", RowKind.Success)
        };
    }

    /// <summary>Describes a row from the final scan (and clean) reports. All numbers come from real results.</summary>
    public static (string Text, RowKind Kind) Describe(
        CleanerGroup group, ScanReport? scan, CleanReport? clean, int registryRepaired = 0)
    {
        if (scan is null)
        {
            return ("Ready to scan", RowKind.Neutral);
        }

        var modules = scan.Modules.Where(m => m.Group == group).ToList();
        if (modules.Count == 0)
        {
            return ("Ready to scan", RowKind.Neutral);
        }

        var active = modules.Where(m => m.Status != ModuleScanStatus.Disabled).ToList();
        if (active.Count == 0)
        {
            return ("Disabled in settings", RowKind.Neutral);
        }

        if (active.All(m => m.Status == ModuleScanStatus.Unavailable))
        {
            return (active[0].Message ?? "Not available", RowKind.Neutral);
        }

        if (active.Any(m => m.Status == ModuleScanStatus.Canceled))
        {
            return ("Canceled", RowKind.Warning);
        }

        if (active.All(m => m.Status == ModuleScanStatus.Failed))
        {
            return (active[0].Message ?? "Scan failed", RowKind.Error);
        }

        if (group == CleanerGroup.Registry)
        {
            return DescribeRegistry(active, registryRepaired);
        }

        var findings = active.SelectMany(m => m.Findings).ToList();
        var actionable = findings.Where(f => f.IsEligible && f.Action != ActionType.None).ToList();
        var skipReasons = findings.Where(f => !f.IsEligible && f.SkipReason is not null).Select(f => f.SkipReason!).Distinct().ToList();
        var needsAdmin = active.Count(m => m.Status == ModuleScanStatus.Skipped && (m.Message?.Contains("administrator", StringComparison.OrdinalIgnoreCase) ?? false));

        if (clean is not null)
        {
            var results = clean.Modules.Where(m => m.Group == group).ToList();
            var bytes = results.Sum(r => r.BytesRecovered);
            var cleaned = results.Sum(r => r.ItemsCleaned);
            var skipped = results.Sum(r => r.ItemsSkipped);
            var errors = results.Sum(r => r.Errors);

            if (results.Count == 0)
            {
                return DescribeScanOnly(actionable, skipReasons, needsAdmin, active);
            }

            var text = bytes > 0 ? $"Cleaned {ByteFormatter.Format(bytes)}"
                : cleaned > 0 ? (group == CleanerGroup.Privacy ? $"Cleared {cleaned:N0} entries" : $"Completed ({cleaned:N0} items)")
                : "Nothing cleaned";
            if (skipped > 0)
            {
                text += $" · {skipped:N0} skipped";
            }

            if (errors > 0)
            {
                text += $" · {errors:N0} error{(errors == 1 ? string.Empty : "s")}";
            }

            return (text, errors > 0 ? RowKind.Error : skipped > 0 ? RowKind.Warning : RowKind.Success);
        }

        return DescribeScanOnly(actionable, skipReasons, needsAdmin, active);
    }

    private static (string, RowKind) DescribeScanOnly(List<Finding> actionable, List<string> skipReasons, int needsAdmin, List<ModuleScanResult> active)
    {
        if (actionable.Count == 0)
        {
            if (skipReasons.Count > 0)
            {
                return (skipReasons[0], RowKind.Warning);
            }

            if (needsAdmin > 0 && active.All(m => m.Status == ModuleScanStatus.Skipped))
            {
                return ("Skipped: requires administrator", RowKind.Warning);
            }

            return (active.FirstOrDefault(m => m.Message is not null && m.Status == ModuleScanStatus.Completed)?.Message ?? "Nothing to clean", RowKind.Success);
        }

        var bytes = actionable.Sum(f => f.EstimatedBytes);
        var text = bytes > 0 ? $"{ByteFormatter.Format(bytes)} can be recovered" : $"{actionable.Count:N0} item{(actionable.Count == 1 ? string.Empty : "s")} ready";
        var kind = RowKind.Success;
        if (skipReasons.Count > 0)
        {
            text += " · " + skipReasons[0];
            kind = RowKind.Warning;
        }

        if (needsAdmin > 0)
        {
            text += $" · {needsAdmin} area{(needsAdmin == 1 ? string.Empty : "s")} need administrator";
            kind = RowKind.Warning;
        }

        return (text, kind);
    }

    private static (string, RowKind) DescribeRegistry(List<ModuleScanResult> active, int repaired)
    {
        var count = active.SelectMany(m => m.Findings).Count(f => f.Action == ActionType.RepairRegistryEntry);
        var errors = active.Sum(m => m.Errors.Count);
        if (count == 0)
        {
            return (errors > 0 ? "No issues found (some keys could not be read)" : "No issues found", errors > 0 ? RowKind.Warning : RowKind.Success);
        }

        var text = $"{count} issue{(count == 1 ? string.Empty : "s")} found";
        if (repaired > 0)
        {
            text += $" · {repaired} repaired";
        }

        return (text, RowKind.Warning);
    }
}
