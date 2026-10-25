namespace Clenzy.Core.Models;

/// <summary>Risk classification shown to the user and used by the automatic-clean policy.</summary>
public enum RiskLevel
{
    /// <summary>Regenerable data that applications recreate on demand.</summary>
    Safe = 0,
    /// <summary>Low risk; may cause a one-time slowdown while data is rebuilt.</summary>
    Low = 1,
    /// <summary>Removes user-visible state (for example website data) or is hard to undo.</summary>
    Medium = 2,
    /// <summary>Irreversible and user-visible (for example emptying the Recycle Bin).</summary>
    High = 3
}

/// <summary>The five rows shown on the main screen.</summary>
public enum CleanerGroup
{
    SystemJunk = 0,
    Chrome = 1,
    Edge = 2,
    Registry = 3,
    Privacy = 4
}

public enum ActionType
{
    /// <summary>Informational finding (for example a skipped category). Nothing is executed.</summary>
    None = 0,
    /// <summary>Permanently deletes a single file inside an approved root.</summary>
    DeleteFile = 1,
    /// <summary>Empties the Windows Recycle Bin through the shell API (permanent).</summary>
    EmptyRecycleBin = 2,
    /// <summary>Removes the values of a clearly scoped history key (privacy cleanup).</summary>
    ClearHistoryValues = 3,
    /// <summary>Runs a fixed, supported Windows servicing command.</summary>
    RunServicing = 4,
    /// <summary>Registry repair. Only ever executed by the registry repair workflow, never by the engine.</summary>
    RepairRegistryEntry = 5
}

public enum ModuleScanStatus
{
    Completed = 0,
    Skipped = 1,
    Unavailable = 2,
    Failed = 3,
    Canceled = 4,
    Disabled = 5
}

public enum EnginePhase
{
    Idle = 0,
    Scanning = 1,
    Cleaning = 2,
    Completed = 3,
    Canceled = 4,
    Failed = 5
}

public enum GroupState
{
    Waiting = 0,
    Scanning = 1,
    Scanned = 2,
    Cleaning = 3,
    Cleaned = 4,
    Skipped = 5,
    Unavailable = 6,
    Disabled = 7,
    Failed = 8,
    Canceled = 9
}

public enum PlanMode
{
    /// <summary>One-click mode: only findings that are explicitly safe for unattended cleaning.</summary>
    Automatic = 0,
    /// <summary>Review mode: every eligible finding the user left selected.</summary>
    Reviewed = 1
}

public enum OutcomeKind
{
    Cleaned = 0,
    Skipped = 1,
    Error = 2
}
