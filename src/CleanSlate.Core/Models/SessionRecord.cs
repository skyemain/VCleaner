namespace CleanSlate.Core.Models;

public sealed class SessionCategoryRecord
{
    public string Module { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int ItemsFound { get; set; }
    public int ItemsCleaned { get; set; }
    public int ItemsSkipped { get; set; }
    public int Errors { get; set; }
    public long EstimatedBytes { get; set; }
    public long RecoveredBytes { get; set; }
    public string? Note { get; set; }
}

public sealed class SessionRegistryChange
{
    public string Location { get; set; } = string.Empty;
    public string Rule { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? BackupId { get; set; }
}

/// <summary>A persisted record of one scan/clean session. Contains no browser data or personal content.</summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ScanTimestamp { get; set; }
    public DateTimeOffset? CleanupTimestamp { get; set; }
    public string Mode { get; set; } = "Automatic";
    public bool WasElevated { get; set; }
    public bool WasCanceled { get; set; }
    public double DurationSeconds { get; set; }
    public long TotalEstimatedBytes { get; set; }
    public long TotalRecoveredBytes { get; set; }
    public int ItemsCleaned { get; set; }
    public int ItemsSkipped { get; set; }
    public int Errors { get; set; }
    public int RegistryIssuesDetected { get; set; }
    public int RegistryIssuesRepaired { get; set; }
    public List<SessionCategoryRecord> Categories { get; set; } = new();
    public List<SessionRegistryChange> RegistryChanges { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}
