using Clenzy.Cleaners;
using Clenzy.Cleaners.Registry;
using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Services;

namespace Clenzy.TestSupport;

/// <summary>Uses the real local file system (for module tests that operate on temporary fixtures).</summary>
public sealed class RealFileProbe : IFileProbe
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool IsOnFixedLocalDrive(string path) => true;
}

/// <summary>Wires the complete engine against a temporary fixture directory, fakes and an in-memory registry.</summary>
public sealed class Harness : IDisposable
{
    public Harness(RegistryScanLocations? registryLocations = null)
    {
        Temp = new TempDirectory();
        Paths = TestPaths.Create(Temp.Path);
        AppData = new AppDataLayout(Temp.Combine("AppData-Clenzy"));
        AppData.EnsureCreated();

        Protected = new ProtectedPaths(Paths);
        Deletion = new SafeDeletionService(Protected);
        Log = new RecordingLogger();
        Registry = new InMemoryRegistry();
        RegistryProbe = new FakeFileProbe();
        Env = new FakeEnvironment();
        Scanner = new RegistryScanner(Registry, RegistryProbe, Env, registryLocations);
        Gate = new RunGate();
        Privilege = new FakePrivilege();
        Backups = new RegistryBackupService(Registry, AppData, Log);
        RestorePoint = new FakeRestorePoint();
        Repair = new RegistryRepairService(Registry, Scanner, Backups, RestorePoint, Privilege, Gate, Log);

        Closer = new FakeBrowserCloser(Browser);
        Modules = CleanerCatalog.CreateModules(Paths, Deletion, Registry, Scanner, FileProbe, Runner, Bin, Browser, Closer, Log);
        Engine = new CleaningEngine(Modules, Privilege, Log, Gate);
    }

    public TempDirectory Temp { get; }
    public SystemPaths Paths { get; }
    public AppDataLayout AppData { get; }
    public ProtectedPaths Protected { get; }
    public SafeDeletionService Deletion { get; }
    public RecordingLogger Log { get; }
    public InMemoryRegistry Registry { get; }
    public FakeFileProbe RegistryProbe { get; }
    public RealFileProbe FileProbe { get; } = new();
    public FakeEnvironment Env { get; }
    public RegistryScanner Scanner { get; }
    public RunGate Gate { get; }
    public FakePrivilege Privilege { get; }
    public RegistryBackupService Backups { get; }
    public FakeRestorePoint RestorePoint { get; }
    public RegistryRepairService Repair { get; }
    public FakeRecycleBin Bin { get; } = new();
    public FakeProcessRunner Runner { get; } = new();
    public FakeBrowserDetector Browser { get; } = new();
    public FakeBrowserCloser Closer { get; }
    public IReadOnlyList<ICleanerModule> Modules { get; }
    public CleaningEngine Engine { get; }

    /// <summary>Settings with only the requested features enabled (everything else off).</summary>
    public static ClenzySettings Minimal(Action<ClenzySettings>? configure = null)
    {
        var s = new ClenzySettings
        {
            CleanUserTemp = false,
            CleanWindowsTemp = false,
            CleanRecentTempFiles = false,
            CleanPrefetch = false,
            CloseBrowsersBeforeCleaning = false,
            CleanThumbnailCache = false,
            CleanShaderCache = false,
            CleanErrorReports = false,
            CleanCrashDumps = false,
            CleanWindowsLogs = false,
            CleanChromeCache = false,
            CleanEdgeCache = false,
            ScanRegistry = false
        };
        configure?.Invoke(s);
        return s;
    }

    public static readonly DateTime OldUtc = DateTime.UtcNow.AddDays(-60);

    public ICleanerModule Module(string id) => Modules.Single(m => m.ModuleId == id);

    public static string SnapshotTree(string root) =>
        string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}"
                : p));

    public void Dispose() => Temp.Dispose();
}
