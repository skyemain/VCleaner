namespace Clenzy.Core.Models;

/// <summary>
/// A single strongly-typed scan finding (one file, one registry entry, one servicing action, ...).
/// File findings are deliberately compact because a scan may contain many of them.
/// </summary>
public sealed class Finding
{
    public long Id { get; init; }
    public required string ModuleId { get; init; }
    public required CleanerGroup Group { get; init; }
    public required string Category { get; init; }

    /// <summary>File path, registry key/value path, or a short logical location.</summary>
    public required string Location { get; init; }

    public required string Description { get; init; }
    public long EstimatedBytes { get; init; }
    public RiskLevel Risk { get; init; }
    public ActionType Action { get; init; }

    /// <summary>User-controllable selection. Never true for ineligible findings.</summary>
    public bool IsSelected { get; set; }

    public bool IsEligible { get; init; } = true;
    public string? SkipReason { get; init; }

    /// <summary>True when the finding is explicitly classified as safe for unattended (one-click) cleaning.</summary>
    public bool AutoCleanAllowed { get; init; }

    /// <summary>
    /// True when the finding may only run after the user has reviewed and confirmed it
    /// (permanent, user-visible or hard to undo).
    /// </summary>
    public bool ConfirmationRequired { get; init; }

    /// <summary>True when execution permanently removes data with no recovery.</summary>
    public bool IsPermanent { get; init; } = true;

    /// <summary>Identifier of the file rule that produced the finding (file findings only).</summary>
    public string? RuleId { get; init; }

    /// <summary>File identity captured at scan time and compared again immediately before deletion.</summary>
    public long LastWriteUtcTicks { get; init; }

    /// <summary>Module-specific data (for example a <c>RegistryIssue</c>).</summary>
    public object? Payload { get; init; }

    public override string ToString() => $"{ModuleId}/{Category}: {Location}";
}

public sealed record CategorySummary(
    string ModuleId,
    CleanerGroup Group,
    string Category,
    int ItemCount,
    long Bytes,
    bool IsEligible,
    string? SkipReason,
    RiskLevel Risk,
    bool IsPermanent);

public sealed record SkippedSample(string Location, string Reason);

public sealed class ModuleScanResult
{
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public required CleanerGroup Group { get; init; }
    public ModuleScanStatus Status { get; init; } = ModuleScanStatus.Completed;
    public string? Message { get; init; }
    public List<Finding> Findings { get; init; } = new();
    public long ItemsScanned { get; init; }

    /// <summary>Files that matched a rule but were ineligible during the scan (kept as a count plus a few samples).</summary>
    public int SkippedItemCount { get; init; }

    public List<SkippedSample> SkippedSamples { get; init; } = new();
    public List<string> Errors { get; init; } = new();
    public bool FindingsTruncated { get; init; }

    public long EligibleBytes => Findings.Where(f => f.IsEligible).Sum(f => f.EstimatedBytes);
    public int EligibleCount => Findings.Count(f => f.IsEligible && f.Action != ActionType.None);

    public static ModuleScanResult Empty(string id, string name, CleanerGroup group, ModuleScanStatus status, string? message) =>
        new() { ModuleId = id, ModuleName = name, Group = group, Status = status, Message = message };
}

public sealed class ScanReport
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public bool WasCanceled { get; init; }
    public bool IsElevated { get; init; }
    public IReadOnlyList<ModuleScanResult> Modules { get; init; } = Array.Empty<ModuleScanResult>();

    public IEnumerable<Finding> AllFindings => Modules.SelectMany(m => m.Findings);
    public long TotalItemsScanned => Modules.Sum(m => m.ItemsScanned);

    /// <summary>Estimated bytes of eligible, selected findings (never double counts a path claimed by two modules).</summary>
    public long SelectedBytes => AllFindings.Where(f => f.IsEligible && f.IsSelected).Sum(f => f.EstimatedBytes);

    public long EligibleBytes => Modules.Sum(m => m.EligibleBytes);

    public IEnumerable<CategorySummary> GetCategorySummaries()
    {
        foreach (var module in Modules)
        {
            foreach (var g in module.Findings.GroupBy(f => f.Category))
            {
                var first = g.First();
                var actionable = g.Where(f => f.Action != ActionType.None && f.IsEligible).ToList();
                var skipped = g.FirstOrDefault(f => !f.IsEligible);
                yield return new CategorySummary(
                    module.ModuleId,
                    module.Group,
                    g.Key,
                    actionable.Count,
                    actionable.Sum(f => f.EstimatedBytes),
                    actionable.Count > 0,
                    actionable.Count == 0 ? skipped?.SkipReason : null,
                    g.Max(f => f.Risk),
                    first.IsPermanent);
            }
        }
    }
}

public readonly record struct ValidationResult(bool IsValid, string? Reason)
{
    public static ValidationResult Valid { get; } = new(true, null);
    public static ValidationResult Invalid(string reason) => new(false, reason);
}
