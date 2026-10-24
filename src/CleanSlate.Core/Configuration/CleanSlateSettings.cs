using System.Text.Json.Serialization;

namespace CleanSlate.Core.Configuration;

/// <summary>User settings persisted to <c>%LOCALAPPDATA%\CleanSlate\settings.json</c>. Contains no credentials.</summary>
public sealed class CleanSlateSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    // General
    public bool FollowSystemTheme { get; set; } = true;
    public bool LaunchOnStartup { get; set; }
    public bool MinimizeToTray { get; set; }

    /// <summary>At sign-in: open, wait a few seconds, scan, clean what is safe unattended, then close. Needs <see cref="LaunchOnStartup"/>.</summary>
    public bool AutoCleanOnStartup { get; set; }

    /// <summary>Close the app after an automatic clean once the result has been shown.</summary>
    public bool CloseAfterAutoClean { get; set; } = true;

    /// <summary>Start through an elevated scheduled task (no UAC prompt at sign-in) instead of the normal startup entry.</summary>
    public bool ElevatedStartup { get; set; }
    public bool ShowCompletionNotifications { get; set; } = true;

    // Cleaning - system junk
    public bool CleanUserTemp { get; set; } = true;
    public bool CleanWindowsTemp { get; set; } = true;

    /// <summary>When true, temporary files are removed whatever their age (files in use are always skipped). When false, only files older than 24 hours.</summary>
    public bool CleanRecentTempFiles { get; set; } = true;

    /// <summary>Windows prefetch (*.pf) files. Windows recreates them; apps may start slightly slower once. Requires administrator.</summary>
    public bool CleanPrefetch { get; set; } = true;
    public bool CleanThumbnailCache { get; set; } = true;
    public bool CleanShaderCache { get; set; } = true;
    public bool CleanErrorReports { get; set; } = true;
    public bool CleanCrashDumps { get; set; } = true;
    public bool CleanWindowsLogs { get; set; } = true;
    public bool EmptyRecycleBin { get; set; }
    public bool CleanWindowsUpdateComponents { get; set; }
    public bool CleanDeliveryOptimization { get; set; }

    // Cleaning - browsers
    public bool CleanChromeCache { get; set; } = true;
    public bool CleanEdgeCache { get; set; } = true;
    public bool ClearBrowserSiteStorage { get; set; }

    /// <summary>Close Chrome/Edge (gently first, then forcefully) so their caches can be cleaned. Sign-ins, passwords and cookies are never touched.</summary>
    public bool CloseBrowsersBeforeCleaning { get; set; } = true;

    /// <summary>Show one confirmation naming the browsers that are about to be closed.</summary>
    public bool AskBeforeClosingBrowsers { get; set; } = true;

    /// <summary>Unsupported: requires editing live SQLite databases. Always treated as false.</summary>
    public bool ClearBrowserHistory { get; set; }

    /// <summary>Unsupported: requires editing live SQLite databases. Always treated as false.</summary>
    public bool ClearBrowserCookies { get; set; }

    // Cleaning - privacy (explicit opt-in)
    public bool ClearActivityHistory { get; set; }
    public bool ClearRunHistory { get; set; } = true;
    public bool ClearRecentItems { get; set; } = true;
    public bool ClearExplorerAddressHistory { get; set; } = true;

    public bool ReviewBeforeCleaning { get; set; }

    // Registry
    public bool ScanRegistry { get; set; } = true;
    public bool CreateRestorePoint { get; set; } = true;

    // Advanced
    public List<string> ExcludedFolders { get; set; } = new();
    public List<string> ExcludedFiles { get; set; } = new();

    [JsonIgnore]
    public bool EffectiveClearBrowserHistory => false;

    [JsonIgnore]
    public bool EffectiveClearBrowserCookies => false;

    public CleanSlateSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        FollowSystemTheme = FollowSystemTheme,
        LaunchOnStartup = LaunchOnStartup,
        MinimizeToTray = MinimizeToTray,
        AutoCleanOnStartup = AutoCleanOnStartup,
        CloseAfterAutoClean = CloseAfterAutoClean,
        ElevatedStartup = ElevatedStartup,
        ShowCompletionNotifications = ShowCompletionNotifications,
        CleanUserTemp = CleanUserTemp,
        CleanWindowsTemp = CleanWindowsTemp,
        CleanRecentTempFiles = CleanRecentTempFiles,
        CleanPrefetch = CleanPrefetch,
        CleanThumbnailCache = CleanThumbnailCache,
        CleanShaderCache = CleanShaderCache,
        CleanErrorReports = CleanErrorReports,
        CleanCrashDumps = CleanCrashDumps,
        CleanWindowsLogs = CleanWindowsLogs,
        EmptyRecycleBin = EmptyRecycleBin,
        CleanWindowsUpdateComponents = CleanWindowsUpdateComponents,
        CleanDeliveryOptimization = CleanDeliveryOptimization,
        CleanChromeCache = CleanChromeCache,
        CleanEdgeCache = CleanEdgeCache,
        ClearBrowserSiteStorage = ClearBrowserSiteStorage,
        CloseBrowsersBeforeCleaning = CloseBrowsersBeforeCleaning,
        AskBeforeClosingBrowsers = AskBeforeClosingBrowsers,
        ClearBrowserHistory = false,
        ClearBrowserCookies = false,
        ClearActivityHistory = ClearActivityHistory,
        ClearRunHistory = ClearRunHistory,
        ClearRecentItems = ClearRecentItems,
        ClearExplorerAddressHistory = ClearExplorerAddressHistory,
        ReviewBeforeCleaning = ReviewBeforeCleaning,
        ScanRegistry = ScanRegistry,
        CreateRestorePoint = CreateRestorePoint,
        ExcludedFolders = new List<string>(ExcludedFolders),
        ExcludedFiles = new List<string>(ExcludedFiles)
    };

    /// <summary>Clamps and sanitizes values loaded from disk.</summary>
    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        ClearBrowserHistory = false;
        ClearBrowserCookies = false;
        ExcludedFolders = CleanList(ExcludedFolders);
        ExcludedFiles = CleanList(ExcludedFiles);
    }

    private static List<string> CleanList(List<string>? values) =>
        (values ?? new List<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .ToList();
}
