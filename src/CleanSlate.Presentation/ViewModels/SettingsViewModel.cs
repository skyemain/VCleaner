using System.Collections.ObjectModel;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.Presentation.ViewModels;

/// <summary>A single boolean setting. Turning on a risky option first asks for explicit consent.</summary>
public sealed class SettingToggleViewModel : ObservableObject
{
    private readonly Func<CleanSlateSettings, bool> _get;
    private readonly Func<SettingToggleViewModel, bool, Task<bool>> _request;

    public SettingToggleViewModel(
        string key, string label, string description,
        Func<CleanSlateSettings, bool> get,
        Func<SettingToggleViewModel, bool, Task<bool>> request,
        string? consentTitle = null, string? consentMessage = null, string? badge = null)
    {
        Key = key;
        Label = label;
        Description = description;
        ConsentTitle = consentTitle;
        ConsentMessage = consentMessage;
        Badge = badge;
        _get = get;
        _request = request;
    }

    public string Key { get; }

    public string Label { get; }

    public string Description { get; }

    public string? ConsentTitle { get; }

    public string? ConsentMessage { get; }

    public string? Badge { get; }

    public bool HasBadge => Badge is not null;

    public CleanSlateSettings? Source { get; set; }

    public bool IsOn
    {
        get => Source is not null && _get(Source);
        set => _ = SetAsync(value);
    }

    public async Task SetAsync(bool value)
    {
        if (IsOn == value)
        {
            return;
        }

        await _request(this, value).ConfigureAwait(true);
        OnPropertyChanged(nameof(IsOn));
    }

    public void Refresh() => OnPropertyChanged(nameof(IsOn));
}

public sealed class SettingsSectionViewModel
{
    public SettingsSectionViewModel(string title, string? description, IReadOnlyList<SettingToggleViewModel> toggles)
    {
        Title = title;
        Description = description;
        Toggles = toggles;
    }

    public string Title { get; }

    public string? Description { get; }

    public IReadOnlyList<SettingToggleViewModel> Toggles { get; }
}

public sealed record UnsupportedFeature(string Name, string Reason);

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(SessionRecord record)
    {
        Record = record;
        When = (record.CleanupTimestamp ?? record.ScanTimestamp).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        Summary = record.Mode == "Registry repair"
            ? $"Registry repair · {record.RegistryIssuesRepaired} repaired"
            : record.CleanupTimestamp is null
                ? $"{record.Mode} · scan only · {ByteFormatter.Format(record.TotalEstimatedBytes)} found"
                : $"{record.Mode} · {ByteFormatter.Format(record.TotalRecoveredBytes)} recovered · {record.ItemsSkipped:N0} skipped · {record.Errors:N0} errors";
    }

    public SessionRecord Record { get; }

    public string When { get; }

    public string Summary { get; }
}

public sealed class SettingsViewModel : PageViewModel
{
    private readonly PresentationContext _ctx;
    private readonly Action _openBackups;
    private readonly Func<bool> _isBusy;
    private string _message = string.Empty;

    public SettingsViewModel(PresentationContext ctx, Action back, Action openBackups, Func<bool> isBusy)
        : base("Settings", back)
    {
        _ctx = ctx;
        _openBackups = openBackups;
        _isBusy = isBusy;

        Sections = BuildSections();
        Toggles = Sections.SelectMany(s => s.Toggles).ToList();
        BindToggles();

        UnsupportedFeatures = new[]
        {
            new UnsupportedFeature("Browser history", "Chrome and Edge keep history in live SQLite databases. Editing or deleting those files while the browser may use them risks corrupting the profile, so CleanSlate does not touch history."),
            new UnsupportedFeature("Browser cookies and download history", "Stored in the same live databases and protected for the same reason. Use the browser's own \"Clear browsing data\" for these."),
            new UnsupportedFeature("Saved passwords, autofill and bookmarks", "Never touched. These are personal data, not cache."),
            new UnsupportedFeature("Update check", "CleanSlate does not contact the network. Download new versions yourself and replace the folder.")
        };

        ExcludedFolders = new ObservableCollection<string>(_ctx.Settings.Current.ExcludedFolders);
        ExcludedFiles = new ObservableCollection<string>(_ctx.Settings.Current.ExcludedFiles);
        History = new ObservableCollection<HistoryItemViewModel>();

        AddExcludedFolderCommand = new RelayCommand(AddExcludedFolder);
        AddExcludedFileCommand = new RelayCommand(AddExcludedFile);
        RemoveExclusionCommand = new RelayCommand(p => RemoveExclusion(p as string));
        OpenLogsCommand = new RelayCommand(() => _ctx.Dialogs.OpenFolder(_ctx.Logs.LogsDirectory));
        ExportReportCommand = new RelayCommand(ExportReport);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, null, OnError);
        ResetCommand = new AsyncRelayCommand(ResetAsync, null, OnError);
        OpenBackupsCommand = new RelayCommand(_openBackups);
        RefreshHistory();
    }

    public IReadOnlyList<SettingsSectionViewModel> Sections { get; }

    public IReadOnlyList<SettingToggleViewModel> Toggles { get; }

    public IReadOnlyList<UnsupportedFeature> UnsupportedFeatures { get; }

    public ObservableCollection<string> ExcludedFolders { get; }

    public ObservableCollection<string> ExcludedFiles { get; }

    public ObservableCollection<HistoryItemViewModel> History { get; }

    public RelayCommand AddExcludedFolderCommand { get; }

    public RelayCommand AddExcludedFileCommand { get; }

    public RelayCommand RemoveExclusionCommand { get; }

    public RelayCommand OpenLogsCommand { get; }

    public RelayCommand ExportReportCommand { get; }

    public AsyncRelayCommand ClearHistoryCommand { get; }

    public AsyncRelayCommand ResetCommand { get; }

    public RelayCommand OpenBackupsCommand { get; }

    public string VersionText => $"CleanSlate {_ctx.VersionText}";

    public string PrivilegeText => _ctx.Elevation.IsElevated ? "Running as administrator" : "Running with standard user rights";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public SettingToggleViewModel Toggle(string key) => Toggles.Single(t => t.Key == key);

    private void OnError(Exception ex)
    {
        _ctx.Log.Error("Settings operation failed", ex);
        Message = "That did not work: " + ex.Message;
    }

    private void BindToggles()
    {
        foreach (var toggle in Toggles)
        {
            toggle.Source = _ctx.Settings.Current;
        }
    }

    private IReadOnlyList<SettingsSectionViewModel> BuildSections()
    {
        SettingToggleViewModel T(string key, string label, string description, Func<CleanSlateSettings, bool> get, Action<CleanSlateSettings, bool> set,
            string? consentTitle = null, string? consentMessage = null, string? badge = null) =>
            new(key, label, description, get, (self, value) => ApplyAsync(self, value, set), consentTitle, consentMessage, badge);

        return new List<SettingsSectionViewModel>
        {
            new("General", null, new[]
            {
                T("follow-theme", "Follow the Windows light/dark theme", "Turn off to always use the light theme.", s => s.FollowSystemTheme, (s, v) => s.FollowSystemTheme = v),
                T("startup", "Start CleanSlate when I sign in", "Adds a per-user startup entry. CleanSlate never cleans automatically at startup.", s => s.LaunchOnStartup, (s, v) => s.LaunchOnStartup = v),
                T("tray", "Minimize to the notification area", "Keeps CleanSlate in the tray when the window is minimized.", s => s.MinimizeToTray, (s, v) => s.MinimizeToTray = v),
                T("notify", "Show a notification when cleaning finishes", "Only shown when the window is not in front.", s => s.ShowCompletionNotifications, (s, v) => s.ShowCompletionNotifications = v)
            }),
            new("Cleaning behavior", "One-click cleaning only runs categories that are safe to run unattended.", new[]
            {
                T("review", "Always review before cleaning", "Shows every category and lets you deselect items before anything is deleted.", s => s.ReviewBeforeCleaning, (s, v) => s.ReviewBeforeCleaning = v)
            }),
            new("System junk", "Every item is permanently deleted. Only files older than a minimum age and inside approved folders are touched.", new[]
            {
                T("user-temp", "User temporary files", "%TEMP% files that are not in use and are older than the minimum age.", s => s.CleanUserTemp, (s, v) => s.CleanUserTemp = v),
                T("win-temp", "Windows temporary files", "C:\\Windows\\Temp. Requires administrator.", s => s.CleanWindowsTemp, (s, v) => s.CleanWindowsTemp = v, badge: "Administrator"),
                T("thumbs", "Thumbnail cache", "Explorer rebuilds thumbnails on demand.", s => s.CleanThumbnailCache, (s, v) => s.CleanThumbnailCache = v),
                T("shader", "DirectX shader cache", "Games and apps rebuild it; the first launch afterward may be slower.", s => s.CleanShaderCache, (s, v) => s.CleanShaderCache = v),
                T("wer", "Error reports", "Windows Error Reporting archives and queues.", s => s.CleanErrorReports, (s, v) => s.CleanErrorReports = v),
                T("dumps", "Crash dumps", "Memory dumps from crashed applications.", s => s.CleanCrashDumps, (s, v) => s.CleanCrashDumps = v),
                T("logs", "Old Windows logs", "Rotated servicing and update logs older than 30 days. Requires administrator.", s => s.CleanWindowsLogs, (s, v) => s.CleanWindowsLogs = v, badge: "Administrator"),
                T("recycle", "Empty the Recycle Bin", "Permanently deletes everything in the Recycle Bin.", s => s.EmptyRecycleBin, (s, v) => s.EmptyRecycleBin = v,
                    "Empty the Recycle Bin?", "When cleaning, CleanSlate will permanently empty the Recycle Bin after you confirm in the review screen. Deleted items cannot be recovered.", "Permanent"),
                T("wu", "Windows Update cleanup", "Runs the supported Windows component cleanup (DISM). Requires administrator and can take several minutes.", s => s.CleanWindowsUpdateComponents, (s, v) => s.CleanWindowsUpdateComponents = v,
                    "Enable Windows Update cleanup?", "CleanSlate will run the supported command \"dism.exe /Online /Cleanup-Image /StartComponentCleanup\" after you confirm in the review screen. Superseded update components can no longer be uninstalled afterward.", "Administrator"),
                T("do", "Delivery Optimization cache", "Clears downloaded update content shared with other PCs. Requires administrator.", s => s.CleanDeliveryOptimization, (s, v) => s.CleanDeliveryOptimization = v,
                    "Enable Delivery Optimization cleanup?", "CleanSlate will ask Windows to delete its Delivery Optimization cache after you confirm in the review screen.", "Administrator")
            }),
            new("Browsers", "Chrome and Edge are only cleaned while they are closed. Passwords, bookmarks, history and cookies are never touched.", new[]
            {
                T("chrome", "Chrome cache", "HTTP, code and GPU caches of every profile.", s => s.CleanChromeCache, (s, v) => s.CleanChromeCache = v),
                T("edge", "Edge cache", "HTTP, code and GPU caches of every profile.", s => s.CleanEdgeCache, (s, v) => s.CleanEdgeCache = v),
                T("site-storage", "Website storage (Local Storage, IndexedDB, Cache Storage)", "Can sign you out of some sites and removes offline site data. Off by default.", s => s.ClearBrowserSiteStorage, (s, v) => s.ClearBrowserSiteStorage = v,
                    "Clear website storage?", "Website storage includes Local Storage, IndexedDB, Session Storage and Cache Storage. Some sites may sign you out or lose offline data. Cookies are not removed. This is confirmed again in the review screen.", "Confirmation")
            }),
            new("Privacy", "Privacy cleanup is off by default and always shown in the review screen. Privacy entries are not backed up.", new[]
            {
                T("privacy", "Enable privacy cleanup", "Turns on the options below.", s => s.ClearActivityHistory, (s, v) => s.ClearActivityHistory = v,
                    "Enable privacy cleanup?", "This clears Run history, typed Explorer paths and the Recent items list. These entries are not backed up and cannot be restored.", "Permanent"),
                T("run-history", "Run dialog history", "Clears the Win+R list.", s => s.ClearRunHistory, (s, v) => s.ClearRunHistory = v),
                T("recent", "Recent items", "Removes shortcuts from the Recent folder and the recent documents list.", s => s.ClearRecentItems, (s, v) => s.ClearRecentItems = v),
                T("explorer", "Explorer address bar history", "Clears typed paths.", s => s.ClearExplorerAddressHistory, (s, v) => s.ClearExplorerAddressHistory = v)
            }),
            new("Registry", "The registry is never changed during one-click cleaning.", new[]
            {
                T("registry-scan", "Scan the registry for broken entries", "Read-only. Repairs always need your approval on the registry review page.", s => s.ScanRegistry, (s, v) => s.ScanRegistry = v),
                T("restore-point", "Create a restore point before repairs", "Skipped automatically when System Protection is off or Windows declines.", s => s.CreateRestorePoint, (s, v) => s.CreateRestorePoint = v)
            })
        };
    }

    private async Task<bool> ApplyAsync(SettingToggleViewModel toggle, bool value, Action<CleanSlateSettings, bool> set)
    {
        if (_isBusy())
        {
            Message = "Wait for the current operation to finish before changing settings.";
            return false;
        }

        if (value && toggle.ConsentMessage is not null)
        {
            var ok = await _ctx.Dialogs.ConfirmAsync(toggle.ConsentTitle ?? toggle.Label, toggle.ConsentMessage, "Enable").ConfigureAwait(true);
            if (!ok)
            {
                return false;
            }
        }

        var settings = _ctx.Settings.Current;
        var previousStartup = settings.LaunchOnStartup;
        set(settings, value);

        if (toggle.Key == "startup" && !ApplyStartup(value))
        {
            settings.LaunchOnStartup = previousStartup;
            return false;
        }

        try
        {
            _ctx.Settings.Save();
            Message = string.Empty;
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Could not save settings", ex);
            Message = "Settings could not be saved: " + ex.Message;
        }

        return true;
    }

    private bool ApplyStartup(bool enable)
    {
        try
        {
            if (enable)
            {
                var path = _ctx.Lifecycle.ExecutablePath;
                if (string.IsNullOrEmpty(path))
                {
                    Message = "The program path is unknown, so the startup entry was not created.";
                    return false;
                }

                _ctx.Startup.Register(path);
            }
            else
            {
                _ctx.Startup.Unregister();
            }

            return true;
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Could not update the startup entry", ex);
            Message = "Windows refused the startup change: " + ex.Message;
            return false;
        }
    }

    private void AddExcludedFolder()
    {
        var path = _ctx.Dialogs.PickFolder("Choose a folder CleanSlate must never clean");
        AddExclusion(path, folder: true);
    }

    private void AddExcludedFile()
    {
        var path = _ctx.Dialogs.PickFile("Choose a file CleanSlate must never delete");
        AddExclusion(path, folder: false);
    }

    public void AddExclusion(string? path, bool folder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var settings = _ctx.Settings.Current;
        var list = folder ? settings.ExcludedFolders : settings.ExcludedFiles;
        var view = folder ? ExcludedFolders : ExcludedFiles;
        if (list.Contains(path, StringComparer.OrdinalIgnoreCase) || list.Count >= 500)
        {
            return;
        }

        list.Add(path);
        view.Add(path);
        _ctx.Settings.Save();
    }

    public void RemoveExclusion(string? path)
    {
        if (path is null)
        {
            return;
        }

        var settings = _ctx.Settings.Current;
        var removed = settings.ExcludedFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
        removed |= settings.ExcludedFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0;
        foreach (var item in ExcludedFolders.Where(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ExcludedFolders.Remove(item);
        }

        foreach (var item in ExcludedFiles.Where(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            ExcludedFiles.Remove(item);
        }

        if (removed)
        {
            _ctx.Settings.Save();
        }
    }

    public void RefreshHistory()
    {
        History.Clear();
        foreach (var record in _ctx.History.GetRecent(25))
        {
            History.Add(new HistoryItemViewModel(record));
        }
    }

    private void ExportReport()
    {
        var sessions = _ctx.History.GetRecent(100);
        if (sessions.Count == 0)
        {
            Message = "There is no history to export yet.";
            return;
        }

        var path = _ctx.Dialogs.PickSaveFile($"CleanSlate-report-{_ctx.Time.GetLocalNow():yyyyMMdd-HHmm}.txt", "Text report (*.txt)|*.txt");
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, ReportExporter.ToText(sessions));
            Message = "Report saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ctx.Log.Error("Could not export report", ex);
            Message = "The report could not be saved: " + ex.Message;
        }
    }

    private async Task ClearHistoryAsync()
    {
        if (_isBusy())
        {
            Message = "Wait for the current operation to finish first.";
            return;
        }

        var ok = await _ctx.Dialogs.ConfirmAsync(
            "Clear CleanSlate history?",
            "This deletes CleanSlate's own session history and log files. It does not touch registry backups or anything outside CleanSlate's data folder.",
            "Clear history").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        _ctx.History.Clear();
        _ctx.Logs.ClearLogs();
        RefreshHistory();
        Message = "History and logs cleared.";
    }

    private async Task ResetAsync()
    {
        if (_isBusy())
        {
            Message = "Wait for the current operation to finish first.";
            return;
        }

        var ok = await _ctx.Dialogs.ConfirmAsync("Reset settings?", "All settings return to their defaults, including exclusions. History and registry backups are kept.", "Reset").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        var hadStartup = _ctx.Settings.Current.LaunchOnStartup;
        _ctx.Settings.Reset();
        if (hadStartup)
        {
            ApplyStartup(false);
        }

        BindToggles();
        foreach (var toggle in Toggles)
        {
            toggle.Refresh();
        }

        ExcludedFolders.Clear();
        ExcludedFiles.Clear();
        Message = "Settings were reset.";
    }
}
