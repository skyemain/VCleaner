namespace Clenzy.Core.Models;

public enum RegistryHiveKind
{
    CurrentUser = 0,
    LocalMachine = 1
}

public enum RegistryViewKind
{
    /// <summary>The native 64-bit view.</summary>
    Registry64 = 0,

    /// <summary>The 32-bit (WOW6432Node) view.</summary>
    Registry32 = 1
}

public enum RegistryDataKind
{
    String = 0,
    ExpandString = 1,
    MultiString = 2,
    DWord = 3,
    QWord = 4,
    Binary = 5
}

/// <summary>Identifies a registry key independent of the platform implementation.</summary>
public readonly record struct RegistryKeyRef(RegistryHiveKind Hive, RegistryViewKind View, string Path)
{
    public string HiveName => Hive == RegistryHiveKind.CurrentUser ? "HKEY_CURRENT_USER" : "HKEY_LOCAL_MACHINE";

    public string ViewName => View == RegistryViewKind.Registry32 ? "32-bit view" : "64-bit view";

    /// <summary>User-visible path, for example <c>HKEY_CURRENT_USER\Software\...</c>.</summary>
    public string DisplayPath => $"{HiveName}\\{Path}" + (Hive == RegistryHiveKind.LocalMachine ? $" [{ViewName}]" : string.Empty);

    public RegistryKeyRef Child(string name) => this with { Path = string.IsNullOrEmpty(Path) ? name : Path + "\\" + name };

    public override string ToString() => DisplayPath;
}

/// <summary>A raw (unexpanded) registry value.</summary>
public sealed record RegistryData(RegistryDataKind Kind, object Value)
{
    public static RegistryData String(string value) => new(RegistryDataKind.String, value);
    public static RegistryData ExpandString(string value) => new(RegistryDataKind.ExpandString, value);

    /// <summary>The text form for string kinds, otherwise null.</summary>
    public string? AsText => Kind is RegistryDataKind.String or RegistryDataKind.ExpandString ? Value as string : null;

    public bool ValueEquals(RegistryData? other)
    {
        if (other is null || other.Kind != Kind)
        {
            return false;
        }

        return Kind switch
        {
            RegistryDataKind.Binary => ((byte[])Value).AsSpan().SequenceEqual((byte[])other.Value),
            RegistryDataKind.MultiString => ((string[])Value).AsSpan().SequenceEqual((string[])other.Value),
            _ => Equals(Value, other.Value)
        };
    }
}

public enum IssueConfidence
{
    Medium = 1,
    High = 2
}

public enum RegistryRuleKind
{
    StartupEntry = 0,
    AppPathsEntry = 1,
    OrphanedUninstallEntry = 2
}

public enum RegistryRepairAction
{
    DeleteValue = 0,
    DeleteKey = 1
}

/// <summary>A registry problem that was detected by a read-only scan and can be repaired after explicit approval.</summary>
public sealed class RegistryIssue
{
    public required string Id { get; init; }
    public required RegistryRuleKind Rule { get; init; }
    public required string RuleTitle { get; init; }
    public required RegistryKeyRef Key { get; init; }

    /// <summary>The value name for value-level issues; null when the whole (leaf) key is the unit of repair.</summary>
    public string? ValueName { get; init; }

    public required string Reason { get; init; }
    public required string Evidence { get; init; }
    public required IssueConfidence Confidence { get; init; }
    public required RiskLevel Risk { get; init; }
    public required RegistryRepairAction Action { get; init; }
    public required string ProposedAction { get; init; }

    /// <summary>Raw data observed during the scan (value-level issues). Compared again before repairing.</summary>
    public RegistryData? ObservedData { get; init; }

    public bool RequiresElevation => Key.Hive == RegistryHiveKind.LocalMachine;

    public string DisplayLocation => ValueName is null ? Key.DisplayPath : $"{Key.DisplayPath}  ->  {(ValueName.Length == 0 ? "(Default)" : ValueName)}";
}

public sealed record RegistryBackupEntry(string Name, RegistryDataKind Kind, string Data);

/// <summary>Serialized backup of exactly the entry that a repair modifies.</summary>
public sealed class RegistryBackupDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string BackupId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string RuleKind { get; set; } = string.Empty;
    public RegistryHiveKind Hive { get; set; }
    public RegistryViewKind View { get; set; }
    public string KeyPath { get; set; } = string.Empty;

    /// <summary>Null for key-level backups, otherwise the single value that was removed.</summary>
    public string? ValueName { get; set; }

    public bool KeyBackup { get; set; }
    public List<RegistryBackupEntry> Values { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public sealed record RegistryBackupInfo(
    string BackupId,
    DateTimeOffset CreatedAt,
    string Location,
    string RuleKind,
    string FilePath,
    bool IsValid,
    string? Problem);

public enum RegistryRepairStatus
{
    Repaired = 0,
    SkippedNoLongerInvalid = 1,
    SkippedNotSupported = 2,
    SkippedNeedsElevation = 3,
    BackupFailed = 4,
    Failed = 5,
    Canceled = 6
}

public sealed record RegistryRepairItemResult(RegistryIssue Issue, RegistryRepairStatus Status, string Message, string? BackupId);

public sealed class RegistryRepairReport
{
    public required IReadOnlyList<RegistryRepairItemResult> Items { get; init; }
    public string? RestorePointMessage { get; init; }
    public bool RestorePointCreated { get; init; }
    public int Repaired => Items.Count(i => i.Status == RegistryRepairStatus.Repaired);
    public int NotRepaired => Items.Count - Repaired;
}

public sealed record RegistryRestoreResult(bool Success, string Message);
