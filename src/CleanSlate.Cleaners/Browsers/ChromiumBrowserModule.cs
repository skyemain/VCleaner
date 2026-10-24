using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Rules;
using CleanSlate.Core.Services;

namespace CleanSlate.Cleaners.Browsers;

public sealed record BrowserDescriptor(
    string Id,
    string DisplayName,
    CleanerGroup Group,
    string UserDataRelativePath,
    IReadOnlyList<string> ProcessNames,
    string ExecutablePathFragment,
    IReadOnlyList<string> InstallPaths,
    string RunningHint);

public sealed record BrowserProfile(string DirectoryName, string DisplayName, string Path);

public interface IBrowserRunningDetector
{
    /// <summary>True when the browser (including background processes) is using the given user-data folder.</summary>
    bool IsRunning(BrowserDescriptor browser, string userDataDirectory);
}

/// <summary>Features that cannot be implemented safely and are therefore reported as unsupported in the UI.</summary>
public static class BrowserCapabilities
{
    public const string HistoryReason =
        "Not supported. Browsing and download history live in the browser's SQLite databases; CleanSlate will not edit a live " +
        "database or delete a whole database file, and selective row removal is not done safely from outside the browser.";

    public const string CookiesReason =
        "Not supported. Cookies are stored in the browser's SQLite database; CleanSlate will not edit it or delete the whole " +
        "file. Use the browser's own 'Clear browsing data' for cookies.";

    public const string SiteStorageReason =
        "Removes site storage folders (Local Storage, IndexedDB, Session Storage, Cache Storage). Requires the browser to be closed. " +
        "You may be signed out of some websites.";
}

public static class BrowserCatalog
{
    public static BrowserDescriptor Chrome(SystemPaths paths) => new(
        "chrome", "Google Chrome", CleanerGroup.Chrome,
        Path.Combine("Google", "Chrome", "User Data"),
        new[] { "chrome" },
        @"\Google\Chrome\",
        new[]
        {
            Path.Combine(paths.ProgramFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(paths.ProgramFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(paths.LocalAppData, "Google", "Chrome", "Application", "chrome.exe")
        },
        "Close all Chrome windows (and background apps) and scan again.");

    public static BrowserDescriptor Edge(SystemPaths paths) => new(
        "edge", "Microsoft Edge", CleanerGroup.Edge,
        Path.Combine("Microsoft", "Edge", "User Data"),
        new[] { "msedge" },
        @"\Microsoft\Edge\",
        new[]
        {
            Path.Combine(paths.ProgramFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(paths.ProgramFiles, "Microsoft", "Edge", "Application", "msedge.exe")
        },
        "Edge may keep running in the background (for example Startup Boost). Close Edge or turn off background running, then scan again.");
}

public static partial class ChromiumProfileDiscovery
{
    [GeneratedRegex(@"^(Default|Profile \d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileNamePattern();

    /// <summary>Finds valid profile folders (those containing a Preferences file) under a Chromium user-data folder.</summary>
    public static IReadOnlyList<BrowserProfile> Discover(string userDataDirectory, out IReadOnlyList<string> notes)
    {
        var noteList = new List<string>();
        notes = noteList;
        var profiles = new List<BrowserProfile>();

        if (!Directory.Exists(userDataDirectory))
        {
            return profiles;
        }

        var names = ReadProfileNames(userDataDirectory);

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(userDataDirectory).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            noteList.Add("The browser data folder could not be read.");
            return profiles;
        }

        foreach (var dir in directories.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(dir);
            if (!ProfileNamePattern().IsMatch(name))
            {
                continue;
            }

            try
            {
                if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    noteList.Add($"Profile '{name}' is a junction or symbolic link and was ignored.");
                    continue;
                }

                if (!File.Exists(Path.Combine(dir, "Preferences")))
                {
                    noteList.Add($"Profile '{name}' looks incomplete (no Preferences file) and was ignored.");
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                noteList.Add($"Profile '{name}' could not be read and was ignored.");
                continue;
            }

            profiles.Add(new BrowserProfile(name, names.TryGetValue(name, out var display) && !string.IsNullOrWhiteSpace(display) ? display : name, dir));
        }

        return profiles;
    }

    private static Dictionary<string, string> ReadProfileNames(string userDataDirectory)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localState = Path.Combine(userDataDirectory, "Local State");
        try
        {
            if (!File.Exists(localState))
            {
                return map;
            }

            using var stream = new FileStream(localState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("profile", out var profile) &&
                profile.TryGetProperty("info_cache", out var cache) &&
                cache.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in cache.EnumerateObject())
                {
                    if (entry.Value.ValueKind == JsonValueKind.Object &&
                        entry.Value.TryGetProperty("name", out var nameElement) &&
                        nameElement.ValueKind == JsonValueKind.String)
                    {
                        map[entry.Name] = nameElement.GetString() ?? entry.Name;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Names are cosmetic; fall back to the folder names.
        }

        return map;
    }
}

/// <summary>Detects a running browser from its processes and the user-data lock file. Never terminates anything.</summary>
public sealed class ProcessBrowserRunningDetector : IBrowserRunningDetector
{
    public bool IsRunning(BrowserDescriptor browser, string userDataDirectory)
    {
        if (IsLockFileHeld(userDataDirectory))
        {
            return true;
        }

        int currentSession;
        try
        {
            currentSession = Process.GetCurrentProcess().SessionId;
        }
        catch (InvalidOperationException)
        {
            currentSession = -1;
        }

        foreach (var name in browser.ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path is not null)
                    {
                        if (path.Replace('/', '\\').Contains(browser.ExecutablePathFragment, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Cannot inspect the process (for example elevated). Treat a same-session match conservatively.
                    try
                    {
                        if (process.SessionId == currentSession)
                        {
                            return true;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited.
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return false;
    }

    private static bool IsLockFileHeld(string userDataDirectory)
    {
        var lockFile = Path.Combine(userDataDirectory, "lockfile");
        try
        {
            if (!File.Exists(lockFile))
            {
                return false;
            }

            using var stream = new FileStream(lockFile, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>Cleans safe cache categories of a Chromium-based browser (Chrome or Edge), independently per profile.</summary>
public sealed class ChromiumBrowserModule : FileRuleModuleBase
{
    private sealed record CacheDirectory(string RelativePath, bool InUserData, bool RequiresBrowserClosed);

    private sealed record CacheCategory(string Name, string Description, IReadOnlyList<CacheDirectory> Directories, RiskLevel Risk, bool IsSiteStorage);

    private static readonly CacheCategory[] Categories =
    {
        new("Disk cache", "Cached web content", new CacheDirectory[]
        {
            new("Cache", false, true),
            new("Media Cache", false, true)
        }, RiskLevel.Safe, false),

        new("Code cache", "Cached compiled script code", new CacheDirectory[]
        {
            new("Code Cache", false, true),
            new(Path.Combine("Service Worker", "ScriptCache"), false, true)
        }, RiskLevel.Safe, false),

        new("GPU cache", "Cached graphics shaders", new CacheDirectory[]
        {
            new("GPUCache", false, true),
            new("DawnCache", false, true),
            new("DawnGraphiteCache", false, true),
            new("DawnWebGPUCache", false, true),
            new("GrShaderCache", false, true),
            new("ShaderCache", false, true),
            new("GrShaderCache", true, true),
            new("ShaderCache", true, true),
            new("GraphiteDawnCache", true, true)
        }, RiskLevel.Safe, false),

        new("Temporary browser artifacts", "Pending crash report files", new CacheDirectory[]
        {
            new(Path.Combine("Crashpad", "reports"), true, false)
        }, RiskLevel.Safe, false),

        new("Site storage", "Website storage (signs you out of some sites)", new CacheDirectory[]
        {
            new("Local Storage", false, true),
            new("IndexedDB", false, true),
            new("Session Storage", false, true),
            new(Path.Combine("Service Worker", "CacheStorage"), false, true),
            new("File System", false, true)
        }, RiskLevel.Medium, true)
    };

    private readonly BrowserDescriptor _browser;
    private readonly SystemPaths _paths;
    private readonly IBrowserRunningDetector _detector;
    private readonly IFileProbe _probe;

    public ChromiumBrowserModule(
        BrowserDescriptor browser, SystemPaths paths, IBrowserRunningDetector detector, IFileProbe probe,
        ISafeDeletionService deletion, IAppLogger log)
        : base(deletion, log)
    {
        _browser = browser;
        _paths = paths;
        _detector = detector;
        _probe = probe;
    }

    public override string ModuleId => $"browser.{_browser.Id}";
    public override string Name => _browser.DisplayName;
    public override string Description => $"Cache, code cache, GPU cache and temporary artifacts of {_browser.DisplayName}. Bookmarks, passwords, autofill, extensions and settings are never touched.";
    public override CleanerGroup Group => _browser.Group;

    public string UserDataDirectory => Path.Combine(_paths.LocalAppData, _browser.UserDataRelativePath);

    public override bool IsEnabled(CleanSlateSettings settings) =>
        _browser.Id == "chrome"
            ? settings.CleanChromeCache || settings.ClearBrowserSiteStorage
            : settings.CleanEdgeCache || settings.ClearBrowserSiteStorage;

    private static bool CacheEnabled(CleanSlateSettings settings, string browserId) =>
        browserId == "chrome" ? settings.CleanChromeCache : settings.CleanEdgeCache;

    protected override RulePlan BuildPlan(CleanSlateSettings settings, bool elevated)
    {
        var plan = new RulePlan();
        var userData = UserDataDirectory;
        var userDataExists = Directory.Exists(userData);
        var installed = _browser.InstallPaths.Any(_probe.FileExists);

        if (!userDataExists)
        {
            plan.EmptyStatus = installed ? ModuleScanStatus.Completed : ModuleScanStatus.Unavailable;
            plan.Message = installed ? "No browser profiles found" : $"{_browser.DisplayName} is not installed";
            return plan;
        }

        var profiles = ChromiumProfileDiscovery.Discover(userData, out var notes);
        if (profiles.Count == 0)
        {
            plan.EmptyStatus = ModuleScanStatus.Completed;
            plan.Message = notes.Count > 0 ? notes[0] : "No browser profiles found";
            return plan;
        }

        var running = _detector.IsRunning(_browser, userData);
        var userDataScope = new HashSet<string>(PathGuard.Comparer);

        foreach (var category in Categories)
        {
            var enabled = category.IsSiteStorage ? settings.ClearBrowserSiteStorage : CacheEnabled(settings, _browser.Id);
            if (!enabled)
            {
                continue;
            }

            var runningNote = running ? $"Skipped: {_browser.DisplayName} is running. {_browser.RunningHint}" : null;
            var skippedByRunning = false;

            foreach (var directory in category.Directories)
            {
                if (running && directory.RequiresBrowserClosed)
                {
                    skippedByRunning = true;
                    continue;
                }

                if (directory.InUserData)
                {
                    var root = Path.Combine(userData, directory.RelativePath);
                    if (userDataScope.Add(category.Name + "|" + root))
                    {
                        plan.Rules.Add(MakeRule(category, directory, root, "Browser-wide"));
                    }

                    continue;
                }

                foreach (var profile in profiles)
                {
                    plan.Rules.Add(MakeRule(category, directory, Path.Combine(profile.Path, directory.RelativePath), $"Profile '{profile.DisplayName}'"));
                }
            }

            if (skippedByRunning)
            {
                plan.SkippedCategories.Add(new SkippedCategory(category.Name, runningNote!, category.Risk));
            }
        }

        return plan;
    }

    private FileCleanRule MakeRule(CacheCategory category, CacheDirectory directory, string root, string scope) => new()
    {
        Id = $"{ModuleId}|{category.Name}|{root}",
        Category = category.Name,
        Description = $"{_browser.DisplayName} {category.Description.ToLowerInvariant()} ({scope})",
        RootPath = root,
        Recursive = true,
        MinAge = TimeSpan.Zero,
        Risk = category.Risk,
        AutoCleanAllowed = !category.IsSiteStorage,
        ConfirmationRequired = category.IsSiteStorage
    };
}
