using System.Globalization;
using System.Text.Json;
using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Services;

namespace Clenzy.Cleaners.Registry;

public sealed class RegistryBackupException : Exception
{
    public RegistryBackupException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Creates, verifies, lists and loads per-entry registry backups stored as JSON documents.</summary>
public sealed class RegistryBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IRegistryAccess _registry;
    private readonly string _directory;
    private readonly IAppLogger _log;
    private readonly TimeProvider _time;

    public RegistryBackupService(IRegistryAccess registry, AppDataLayout layout, IAppLogger log, TimeProvider? time = null)
    {
        _registry = registry;
        _directory = layout.RegistryBackupsDirectory;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public string BackupDirectory => _directory;

    public static string Encode(RegistryData data) => data.Kind switch
    {
        RegistryDataKind.String or RegistryDataKind.ExpandString => (string)data.Value,
        RegistryDataKind.MultiString => JsonSerializer.Serialize((string[])data.Value),
        RegistryDataKind.DWord => ((int)data.Value).ToString(CultureInfo.InvariantCulture),
        RegistryDataKind.QWord => ((long)data.Value).ToString(CultureInfo.InvariantCulture),
        RegistryDataKind.Binary => Convert.ToBase64String((byte[])data.Value),
        _ => throw new RegistryBackupException("Unsupported registry value type.")
    };

    public static RegistryData Decode(RegistryDataKind kind, string text) => kind switch
    {
        RegistryDataKind.String => new RegistryData(kind, text),
        RegistryDataKind.ExpandString => new RegistryData(kind, text),
        RegistryDataKind.MultiString => new RegistryData(kind, JsonSerializer.Deserialize<string[]>(text) ?? Array.Empty<string>()),
        RegistryDataKind.DWord => new RegistryData(kind, int.Parse(text, CultureInfo.InvariantCulture)),
        RegistryDataKind.QWord => new RegistryData(kind, long.Parse(text, CultureInfo.InvariantCulture)),
        RegistryDataKind.Binary => new RegistryData(kind, Convert.FromBase64String(text)),
        _ => throw new RegistryBackupException("Unsupported registry value type.")
    };

    /// <summary>Writes a backup of exactly the entry that a repair will change and verifies it against the live registry.</summary>
    public (RegistryBackupDocument Document, string FilePath) CreateAndVerify(RegistryIssue issue)
    {
        var now = _time.GetUtcNow();
        var document = new RegistryBackupDocument
        {
            BackupId = $"{now.UtcDateTime:yyyyMMddTHHmmssfff}-{issue.Id}",
            CreatedAt = now,
            RuleKind = issue.Rule.ToString(),
            Hive = issue.Key.Hive,
            View = issue.Key.View,
            KeyPath = issue.Key.Path,
            ValueName = issue.ValueName,
            KeyBackup = issue.ValueName is null,
            Reason = issue.Reason
        };

        try
        {
            if (issue.ValueName is not null)
            {
                var data = _registry.GetValue(issue.Key, issue.ValueName)
                           ?? throw new RegistryBackupException("The value could not be read for backup.");
                document.Values.Add(new RegistryBackupEntry(issue.ValueName, data.Kind, Encode(data)));
            }
            else
            {
                if (_registry.GetSubKeyNames(issue.Key).Count > 0)
                {
                    throw new RegistryBackupException("The key has sub-keys and cannot be backed up as a single entry.");
                }

                foreach (var name in _registry.GetValueNames(issue.Key))
                {
                    var data = _registry.GetValue(issue.Key, name)
                               ?? throw new RegistryBackupException($"The value '{name}' has an unsupported type; the key will not be modified.");
                    document.Values.Add(new RegistryBackupEntry(name, data.Kind, Encode(data)));
                }
            }

            System.IO.Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, document.BackupId + ".json");
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                stream.Flush(true);
            }

            File.Move(temp, path, overwrite: true);
            Verify(path, issue);
            return (document, path);
        }
        catch (RegistryBackupException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or System.Security.SecurityException)
        {
            throw new RegistryBackupException("The backup could not be written or verified: " + ex.Message, ex);
        }
    }

    private void Verify(string path, RegistryIssue issue)
    {
        RegistryBackupDocument? loaded;
        using (var stream = File.OpenRead(path))
        {
            loaded = JsonSerializer.Deserialize<RegistryBackupDocument>(stream, JsonOptions);
        }

        if (loaded is null || loaded.Values.Count == 0 && issue.ValueName is not null)
        {
            throw new RegistryBackupException("The backup file is empty.");
        }

        if (loaded.KeyPath != issue.Key.Path || loaded.Hive != issue.Key.Hive || loaded.View != issue.Key.View)
        {
            throw new RegistryBackupException("The backup file does not match the entry.");
        }

        var liveNames = issue.ValueName is not null
            ? new List<string> { issue.ValueName }
            : _registry.GetValueNames(issue.Key).ToList();

        if (liveNames.Count != loaded.Values.Count)
        {
            throw new RegistryBackupException("The backup does not contain every value of the entry.");
        }

        foreach (var entry in loaded.Values)
        {
            var live = _registry.GetValue(issue.Key, entry.Name);
            if (live is null || !Decode(entry.Kind, entry.Data).ValueEquals(live))
            {
                throw new RegistryBackupException($"Verification failed for value '{entry.Name}'.");
            }
        }
    }

    public IReadOnlyList<RegistryBackupInfo> List()
    {
        var result = new List<RegistryBackupInfo>();
        if (!System.IO.Directory.Exists(_directory))
        {
            return result;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(_directory, "*.json").OrderByDescending(f => f, StringComparer.Ordinal))
        {
            var doc = TryLoad(file, out var problem);
            if (doc is null)
            {
                result.Add(new RegistryBackupInfo(Path.GetFileNameWithoutExtension(file), File.GetLastWriteTimeUtc(file), "Unreadable backup", string.Empty, file, false, problem));
                continue;
            }

            var key = new RegistryKeyRef(doc.Hive, doc.View, doc.KeyPath);
            var location = doc.ValueName is null ? key.DisplayPath : $"{key.DisplayPath} -> {(doc.ValueName.Length == 0 ? "(Default)" : doc.ValueName)}";
            result.Add(new RegistryBackupInfo(doc.BackupId, doc.CreatedAt, location, doc.RuleKind, file, true, null));
        }

        return result;
    }

    public RegistryBackupDocument? Load(string backupId, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(backupId) || backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || backupId.Contains(".."))
        {
            problem = "Invalid backup identifier.";
            return null;
        }

        var file = Path.Combine(_directory, backupId + ".json");
        if (!File.Exists(file))
        {
            problem = "Backup not found.";
            return null;
        }

        return TryLoad(file, out problem);
    }

    private RegistryBackupDocument? TryLoad(string file, out string? problem)
    {
        problem = null;
        try
        {
            using var stream = File.OpenRead(file);
            var doc = JsonSerializer.Deserialize<RegistryBackupDocument>(stream, JsonOptions);
            if (doc is null || doc.SchemaVersion != 1 || string.IsNullOrWhiteSpace(doc.KeyPath))
            {
                problem = "Unsupported or damaged backup.";
                return null;
            }

            return doc;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Registry backup '{Path.GetFileName(file)}' could not be read.");
            problem = "Unreadable backup file.";
            return null;
        }
    }
}

/// <summary>
/// Applies approved registry repairs: validates the target against the allow-list, revalidates the entry,
/// writes and verifies a per-entry backup, then removes only that value or leaf key.
/// </summary>
public sealed class RegistryRepairService
{
    private readonly IRegistryAccess _registry;
    private readonly RegistryScanner _scanner;
    private readonly RegistryBackupService _backups;
    private readonly IRestorePointService _restorePoints;
    private readonly IPrivilegeInfo _privileges;
    private readonly RunGate _gate;
    private readonly IAppLogger _log;

    public RegistryRepairService(
        IRegistryAccess registry, RegistryScanner scanner, RegistryBackupService backups,
        IRestorePointService restorePoints, IPrivilegeInfo privileges, RunGate gate, IAppLogger log)
    {
        _registry = registry;
        _scanner = scanner;
        _backups = backups;
        _restorePoints = restorePoints;
        _privileges = privileges;
        _gate = gate;
        _log = log;
    }

    public IReadOnlyList<RegistryBackupInfo> ListBackups() => _backups.List();

    public async Task<RegistryRepairReport> RepairAsync(
        IReadOnlyList<RegistryIssue> issues, bool createRestorePoint, IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        using var lease = _gate.Enter();
        var results = new List<RegistryRepairItemResult>();
        string? restoreMessage = null;
        var restoreCreated = false;

        if (issues.Count > 0 && createRestorePoint)
        {
            try
            {
                var restore = await _restorePoints.TryCreateAsync("Clenzy registry repair", cancellationToken).ConfigureAwait(false);
                restoreCreated = restore.Created;
                restoreMessage = restore.Message;
                _log.Info($"Restore point: {restore.Message}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                restoreMessage = "A restore point could not be created: " + ex.Message;
                _log.Warning(restoreMessage);
            }
        }

        var done = 0;
        foreach (var issue in issues)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                results.Add(new RegistryRepairItemResult(issue, RegistryRepairStatus.Canceled, "Canceled before this entry was changed.", null));
                continue;
            }

            results.Add(RepairOne(issue));
            progress?.Report((++done, issues.Count));
        }

        return new RegistryRepairReport { Items = results, RestorePointCreated = restoreCreated, RestorePointMessage = restoreMessage };
    }

    private RegistryRepairItemResult RepairOne(RegistryIssue issue)
    {
        if (!_scanner.IsSupportedTarget(issue.Rule, issue.Key, issue.ValueName, issue.Action))
        {
            _log.Warning($"Rejected unsupported registry repair target: {issue.Key.DisplayPath}");
            return new RegistryRepairItemResult(issue, RegistryRepairStatus.SkippedNotSupported, "This location is not a supported repair target.", null);
        }

        if (issue.RequiresElevation && !_privileges.IsElevated)
        {
            return new RegistryRepairItemResult(issue, RegistryRepairStatus.SkippedNeedsElevation, "Requires administrator rights.", null);
        }

        var current = _scanner.Revalidate(issue);
        if (current is null)
        {
            return new RegistryRepairItemResult(issue, RegistryRepairStatus.SkippedNoLongerInvalid, "The entry changed or is no longer invalid, so it was left alone.", null);
        }

        RegistryBackupDocument document;
        try
        {
            (document, _) = _backups.CreateAndVerify(current);
        }
        catch (RegistryBackupException ex)
        {
            _log.Warning($"Registry backup failed for {issue.Key.DisplayPath}: {ex.Message}");
            return new RegistryRepairItemResult(issue, RegistryRepairStatus.BackupFailed, "Not changed: " + ex.Message, null);
        }

        try
        {
            if (current.Action == RegistryRepairAction.DeleteValue)
            {
                _registry.DeleteValue(current.Key, current.ValueName!);
            }
            else
            {
                _registry.DeleteKey(current.Key);
            }

            var stillThere = current.Action == RegistryRepairAction.DeleteValue
                ? _registry.GetValue(current.Key, current.ValueName!) is not null
                : _registry.KeyExists(current.Key);

            if (stillThere)
            {
                return new RegistryRepairItemResult(issue, RegistryRepairStatus.Failed, "The entry could not be removed.", document.BackupId);
            }

            _log.Audit($"REGISTRY REPAIRED {current.DisplayLocation} (backup {document.BackupId})");
            return new RegistryRepairItemResult(issue, RegistryRepairStatus.Repaired, "Removed. A backup was saved.", document.BackupId);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or InvalidOperationException)
        {
            _log.Error($"Registry repair failed for {issue.Key.DisplayPath}", ex);
            return new RegistryRepairItemResult(issue, RegistryRepairStatus.Failed, "Windows refused the change: " + ex.Message, document.BackupId);
        }
    }

    /// <summary>Restores a previously backed-up entry. Existing different data is never overwritten.</summary>
    public RegistryRestoreResult Restore(string backupId)
    {
        using var lease = _gate.Enter();
        var doc = _backups.Load(backupId, out var problem);
        if (doc is null)
        {
            return new RegistryRestoreResult(false, problem ?? "Backup could not be loaded.");
        }

        if (!Enum.TryParse<RegistryRuleKind>(doc.RuleKind, out var rule))
        {
            return new RegistryRestoreResult(false, "The backup has an unknown rule type.");
        }

        var key = new RegistryKeyRef(doc.Hive, doc.View, doc.KeyPath);
        var action = doc.KeyBackup ? RegistryRepairAction.DeleteKey : RegistryRepairAction.DeleteValue;
        if (!_scanner.IsSupportedTarget(rule, key, doc.ValueName, action))
        {
            _log.Warning($"Rejected restore of unsupported target {key.DisplayPath}");
            return new RegistryRestoreResult(false, "This backup targets a location Clenzy does not restore.");
        }

        if (key.Hive == RegistryHiveKind.LocalMachine && !_privileges.IsElevated)
        {
            return new RegistryRestoreResult(false, "Restoring this entry requires administrator rights.");
        }

        try
        {
            var values = doc.Values.Select(v => (v.Name, Data: RegistryBackupService.Decode(v.Kind, v.Data))).ToList();

            foreach (var (name, data) in values)
            {
                var existing = _registry.GetValue(key, name);
                if (existing is not null && !existing.ValueEquals(data))
                {
                    return new RegistryRestoreResult(false, $"'{(name.Length == 0 ? "(Default)" : name)}' already exists with different data, so nothing was overwritten.");
                }
            }

            if (doc.KeyBackup)
            {
                _registry.CreateKey(key);
            }
            else if (!_registry.KeyExists(key))
            {
                return new RegistryRestoreResult(false, "The parent key no longer exists, so the entry was not restored.");
            }

            foreach (var (name, data) in values)
            {
                if (_registry.GetValue(key, name) is null)
                {
                    _registry.SetValue(key, name, data);
                }
            }

            _log.Audit($"REGISTRY RESTORED {key.DisplayPath} from backup {doc.BackupId}");
            return new RegistryRestoreResult(true, "The entry was restored.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or FormatException or JsonException or RegistryBackupException)
        {
            _log.Error($"Registry restore failed for {key.DisplayPath}", ex);
            return new RegistryRestoreResult(false, "Windows refused the change: " + ex.Message);
        }
    }
}
