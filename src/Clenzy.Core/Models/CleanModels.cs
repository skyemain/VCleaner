namespace Clenzy.Core.Models;

/// <summary>The set of findings that will actually be executed.</summary>
public sealed class CleanPlan
{
    public required PlanMode Mode { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }

    /// <summary>Findings that were selected but excluded by policy, with the reason (never executed).</summary>
    public IReadOnlyList<(Finding Finding, string Reason)> Rejected { get; init; } = Array.Empty<(Finding, string)>();

    public long EstimatedBytes => Findings.Sum(f => f.EstimatedBytes);
    public int ActionCount => Findings.Count;
}

public sealed record ActionOutcome(OutcomeKind Kind, string ModuleId, string Category, string Location, string? Message, long Bytes);

public sealed record CategoryResult(
    string ModuleId,
    CleanerGroup Group,
    string Category,
    int Cleaned,
    int Skipped,
    int Errors,
    long EstimatedBytes,
    long RecoveredBytes);

public sealed class ModuleCleanResult
{
    public required string ModuleId { get; init; }
    public required string ModuleName { get; init; }
    public required CleanerGroup Group { get; init; }
    public int ItemsCleaned { get; init; }
    public int ItemsSkipped { get; init; }
    public int Errors { get; init; }
    public long BytesRecovered { get; init; }
    public bool WasCanceled { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<CategoryResult> Categories { get; init; } = Array.Empty<CategoryResult>();

    /// <summary>Skipped items and errors (capped). Successful deletions are not retained individually.</summary>
    public IReadOnlyList<ActionOutcome> Outcomes { get; init; } = Array.Empty<ActionOutcome>();

    public int DirectoriesRemoved { get; init; }
}

public sealed class CleanReport
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public bool WasCanceled { get; init; }
    public IReadOnlyList<ModuleCleanResult> Modules { get; init; } = Array.Empty<ModuleCleanResult>();

    public long BytesRecovered => Modules.Sum(m => m.BytesRecovered);
    public int ItemsCleaned => Modules.Sum(m => m.ItemsCleaned);
    public int ItemsSkipped => Modules.Sum(m => m.ItemsSkipped);
    public int Errors => Modules.Sum(m => m.Errors);
    public TimeSpan Duration => CompletedAt - StartedAt;
}

public sealed record GroupProgress(GroupState State, long Items, long EstimatedBytes, long RecoveredBytes, string? Message);

/// <summary>Immutable progress snapshot. Totals are zero when unknown; UI must then show indeterminate progress.</summary>
public sealed record EngineProgress(
    EnginePhase Phase,
    string Operation,
    long ItemsScanned,
    long EstimatedBytes,
    int ActionsProcessed,
    int ActionsTotal,
    int CategoriesCompleted,
    int CategoriesTotal,
    long BytesRecovered,
    int Skipped,
    int Errors,
    IReadOnlyDictionary<CleanerGroup, GroupProgress> Groups)
{
    public bool IsDeterminate => Phase == EnginePhase.Cleaning && ActionsTotal > 0;
    public double Fraction => IsDeterminate ? Math.Min(1.0, (double)ActionsProcessed / ActionsTotal) : 0;

    public static EngineProgress Idle { get; } = new(
        EnginePhase.Idle, string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        new Dictionary<CleanerGroup, GroupProgress>());
}
