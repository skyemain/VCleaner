using CleanSlate.Core.Configuration;
using CleanSlate.Core.Models;

namespace CleanSlate.Core.Interfaces;

public interface IAppLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);

    /// <summary>Records a deletion or registry change in the audit trail.</summary>
    void Audit(string message);
}

public interface ISettingsStore
{
    CleanSlateSettings Load();
    void Save(CleanSlateSettings settings);
    void Reset();
}

public interface IPrivilegeInfo
{
    bool IsElevated { get; }
}

public interface IRegistryAccess
{
    bool KeyExists(RegistryKeyRef key);
    IReadOnlyList<string> GetSubKeyNames(RegistryKeyRef key);

    /// <summary>Value names, including <c>""</c> when the default value is set.</summary>
    IReadOnlyList<string> GetValueNames(RegistryKeyRef key);

    /// <summary>Returns the raw (unexpanded) value, or null when missing or of an unsupported kind.</summary>
    RegistryData? GetValue(RegistryKeyRef key, string name);

    void SetValue(RegistryKeyRef key, string name, RegistryData data);
    void CreateKey(RegistryKeyRef key);
    void DeleteValue(RegistryKeyRef key, string name);

    /// <summary>Deletes a single key. Throws when the key still has sub-keys (never recursive).</summary>
    void DeleteKey(RegistryKeyRef key);
}

public readonly record struct RecycleBinInfo(long Bytes, long Items);

public interface IRecycleBinService
{
    bool IsSupported { get; }
    RecycleBinInfo Query();

    /// <summary>Permanently empties the Recycle Bin. Throws on failure.</summary>
    void Empty();
}

public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut);

public interface IProcessRunner
{
    /// <summary>Runs a fixed command line with no shell. Callers never pass user-supplied arguments.</summary>
    Task<ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record RestorePointResult(bool Created, string Message);

public interface IRestorePointService
{
    Task<RestorePointResult> TryCreateAsync(string description, CancellationToken cancellationToken);
}

/// <summary>File-existence probing used by the registry scanner so rules can be tested without a real disk.</summary>
public interface IFileProbe
{
    bool FileExists(string path);
    bool DirectoryExists(string path);

    /// <summary>True when the drive that contains the path is a present, fixed local drive.</summary>
    bool IsOnFixedLocalDrive(string path);
}

public interface IEnvironmentExpander
{
    /// <summary>Expands %VAR% references. Unknown variables are left untouched.</summary>
    string Expand(string value);

    /// <summary>Variable lookup (case-insensitive); null when undefined.</summary>
    string? Get(string name);
}

public interface IStartupRegistration
{
    bool IsRegistered { get; }
    void Register(string executablePath);
    void Unregister();
}

public interface IHistoryStore
{
    void Save(SessionRecord session);
    IReadOnlyList<SessionRecord> GetRecent(int count);
    SessionRecord? GetLastCleanup();
    void Clear();
}
