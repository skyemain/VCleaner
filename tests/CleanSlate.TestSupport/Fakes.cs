using System.Collections.Concurrent;
using CleanSlate.Cleaners.Browsers;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;

namespace CleanSlate.TestSupport;

/// <summary>A unique temporary directory that is removed (including read-only files) on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cleanslate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Path = System.IO.Path.GetFullPath(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    public string CreateFile(string relativePath, int size = 100, DateTime? lastWriteUtc = null)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);
        if (lastWriteUtc is not null)
        {
            File.SetLastWriteTimeUtc(full, lastWriteUtc.Value);
        }

        return full;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch (IOException)
                {
                }
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of test fixtures.
        }
    }
}

/// <summary>Builds a <see cref="SystemPaths"/> whose every location lives inside a temporary fixture directory.</summary>
public static class TestPaths
{
    public static SystemPaths Create(string root)
    {
        var profile = System.IO.Path.Combine(root, "Users", "tester");
        var windows = System.IO.Path.Combine(root, "Windows");
        var paths = new SystemPaths
        {
            SystemDriveRoot = root,
            UserProfile = profile,
            LocalAppData = System.IO.Path.Combine(profile, "AppData", "Local"),
            RoamingAppData = System.IO.Path.Combine(profile, "AppData", "Roaming"),
            ProgramData = System.IO.Path.Combine(root, "ProgramData"),
            WindowsDirectory = windows,
            SystemDirectory = System.IO.Path.Combine(windows, "System32"),
            ProgramFiles = System.IO.Path.Combine(root, "Program Files"),
            ProgramFilesX86 = System.IO.Path.Combine(root, "Program Files (x86)"),
            UserTemp = System.IO.Path.Combine(profile, "AppData", "Local", "Temp"),
            Documents = System.IO.Path.Combine(profile, "Documents"),
            Desktop = System.IO.Path.Combine(profile, "Desktop"),
            Downloads = System.IO.Path.Combine(profile, "Downloads"),
            Pictures = System.IO.Path.Combine(profile, "Pictures"),
            Videos = System.IO.Path.Combine(profile, "Videos"),
            Music = System.IO.Path.Combine(profile, "Music")
        };

        foreach (var dir in new[] { paths.UserTemp, paths.RoamingAppData, paths.Documents, paths.Desktop, paths.Downloads, paths.Windows(), paths.SystemDirectory })
        {
            Directory.CreateDirectory(dir);
        }

        return paths;
    }

    private static string Windows(this SystemPaths p) => p.WindowsTemp;
}

public sealed class FakePrivilege : IPrivilegeInfo
{
    public bool IsElevated { get; set; }
}

public sealed class FakeBrowserDetector : IBrowserRunningDetector
{
    public bool Running { get; set; }
    public bool IsRunning(BrowserDescriptor browser, string userDataDirectory) => Running;
}

public sealed class FakeRecycleBin : IRecycleBinService
{
    public bool IsSupported { get; set; } = true;
    public long Bytes { get; set; }
    public long Items { get; set; }
    public int EmptyCalls { get; private set; }
    public bool FailOnEmpty { get; set; }

    public RecycleBinInfo Query() => new(Bytes, Items);

    public void Empty()
    {
        EmptyCalls++;
        if (FailOnEmpty)
        {
            throw new IOException("Simulated failure");
        }

        Bytes = 0;
        Items = 0;
    }
}

public sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string File, string Args)> Calls { get; } = new();
    public int ExitCode { get; set; }

    public Task<ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments));
        return Task.FromResult(new ProcessResult(ExitCode, string.Empty, false));
    }
}

public sealed class FakeRestorePoint : IRestorePointService
{
    public int Calls { get; private set; }
    public bool Succeed { get; set; } = true;

    public Task<RestorePointResult> TryCreateAsync(string description, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new RestorePointResult(Succeed, Succeed ? "Restore point created." : "Restore points are unavailable."));
    }
}

public sealed class RecordingLogger : IAppLogger
{
    public ConcurrentQueue<string> Lines { get; } = new();
    public void Info(string message) => Lines.Enqueue("INFO " + message);
    public void Warning(string message) => Lines.Enqueue("WARN " + message);
    public void Error(string message, Exception? exception = null) => Lines.Enqueue("ERROR " + message);
    public void Audit(string message) => Lines.Enqueue("AUDIT " + message);
}

/// <summary>Windows-style file system for resolver tests; paths are plain strings.</summary>
public sealed class FakeFileProbe : IFileProbe
{
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fixedDrives = new(StringComparer.OrdinalIgnoreCase) { "C:" };

    public FakeFileProbe AddFile(string path)
    {
        _files.Add(path);
        return this;
    }

    public FakeFileProbe AddDirectory(string path)
    {
        _directories.Add(path.TrimEnd('\\'));
        return this;
    }

    public FakeFileProbe AddFixedDrive(string drive)
    {
        _fixedDrives.Add(drive);
        return this;
    }

    public bool FileExists(string path) => _files.Contains(path);

    public bool DirectoryExists(string path) => _directories.Contains(path.TrimEnd('\\'));

    public bool IsOnFixedLocalDrive(string path) => path.Length >= 2 && path[1] == ':' && _fixedDrives.Contains(path[..2]);
}

public sealed class FakeEnvironment : IEnvironmentExpander
{
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = @"C:\Windows",
        ["windir"] = @"C:\Windows",
        ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)",
        ["LOCALAPPDATA"] = @"C:\Users\tester\AppData\Local",
        ["APPDATA"] = @"C:\Users\tester\AppData\Roaming",
        ["PATH"] = @"C:\Windows\System32;C:\Windows"
    };

    public FakeEnvironment Set(string name, string value)
    {
        _vars[name] = value;
        return this;
    }

    public string? Get(string name) => _vars.TryGetValue(name, out var v) ? v : null;

    public string Expand(string value)
    {
        var result = value;
        foreach (var (name, replacement) in _vars)
        {
            result = result.Replace("%" + name + "%", replacement, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}

/// <summary>Thread-safe in-memory registry. Never touches a real hive.</summary>
public sealed class InMemoryRegistry : IRegistryAccess
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, RegistryData>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public int WriteCount { get; private set; }

    private static string Id(RegistryKeyRef key) => $"{key.Hive}|{key.View}|{key.Path.Trim('\\')}";

    public void Seed(RegistryKeyRef key, string name, RegistryData data)
    {
        lock (_gate)
        {
            EnsureKey(key)[name] = data;
        }
    }

    public void SeedKey(RegistryKeyRef key)
    {
        lock (_gate)
        {
            EnsureKey(key);
        }
    }

    private Dictionary<string, RegistryData> EnsureKey(RegistryKeyRef key)
    {
        var parts = key.Path.Trim('\\').Split('\\');
        var current = string.Empty;
        Dictionary<string, RegistryData>? last = null;
        foreach (var part in parts)
        {
            current = current.Length == 0 ? part : current + "\\" + part;
            var id = Id(key with { Path = current });
            if (!_keys.TryGetValue(id, out last))
            {
                last = new Dictionary<string, RegistryData>(StringComparer.OrdinalIgnoreCase);
                _keys[id] = last;
            }
        }

        return last!;
    }

    public bool KeyExists(RegistryKeyRef key)
    {
        lock (_gate)
        {
            return _keys.ContainsKey(Id(key));
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryKeyRef key)
    {
        lock (_gate)
        {
            var prefix = Id(key) + "\\";
            return _keys.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.IndexOf('\\', prefix.Length) < 0)
                .Select(k => k[prefix.Length..])
                .ToList();
        }
    }

    public IReadOnlyList<string> GetValueNames(RegistryKeyRef key)
    {
        lock (_gate)
        {
            return _keys.TryGetValue(Id(key), out var values) ? values.Keys.ToList() : new List<string>();
        }
    }

    public RegistryData? GetValue(RegistryKeyRef key, string name)
    {
        lock (_gate)
        {
            return _keys.TryGetValue(Id(key), out var values) && values.TryGetValue(name, out var data) ? data : null;
        }
    }

    public void SetValue(RegistryKeyRef key, string name, RegistryData data)
    {
        lock (_gate)
        {
            WriteCount++;
            EnsureKey(key)[name] = data;
        }
    }

    public void CreateKey(RegistryKeyRef key)
    {
        lock (_gate)
        {
            WriteCount++;
            EnsureKey(key);
        }
    }

    public void DeleteValue(RegistryKeyRef key, string name)
    {
        lock (_gate)
        {
            WriteCount++;
            if (_keys.TryGetValue(Id(key), out var values))
            {
                values.Remove(name);
            }
        }
    }

    public void DeleteKey(RegistryKeyRef key)
    {
        lock (_gate)
        {
            WriteCount++;
            if (GetSubKeyNames(key).Count > 0)
            {
                throw new InvalidOperationException("Key has sub-keys.");
            }

            _keys.Remove(Id(key));
        }
    }
}
