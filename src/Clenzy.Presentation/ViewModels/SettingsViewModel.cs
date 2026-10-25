using System.Collections.ObjectModel;
using Clenzy.Core.Configuration;
using Clenzy.Core.Models;
using Clenzy.Core.Services;
using Clenzy.Presentation.Infrastructure;

namespace Clenzy.Presentation.ViewModels;

/// <summary>A single boolean setting. Turning on a risky option first asks for explicit consent.</summary>
public sealed class SettingToggleViewModel : ObservableObject
{
    private readonly Func<ClenzySettings, bool> _get;
    private readonly Func<SettingToggleViewModel, bool, Task<bool>> _request;

    public SettingToggleViewModel(
        string key, string label, string description,
        Func<ClenzySettings, bool> get,
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

    public ClenzySettings? Source { get; set; }

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
    private string _installStatusText = string.Empty;
    private string _installButtonText = "Install";
    private InstallStatus? _installStatus;
    private CertificateStatus? _certificateStatus;
    private string _certificateText = string.Empty;

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
            new UnsupportedFeature("Browser history", "Chrome and Edge keep history in live SQLite databases. Editing or deleting those files while the browser may use them risks corrupting the profile, so Clenzy does not touch history."),
            new UnsupportedFeature("Browser cookies and download history", "Stored in the same live databases and protected for the same reason. Use the browser's own \"Clear browsing data\" for these."),
            new UnsupportedFeature("Saved passwords, autofill and bookmarks", "Never touched. These are personal data, not cache."),
            new UnsupportedFeature("Update check", "Clenzy does not contact the network. To update, run the newer Clenzy.exe and choose Install: it replaces the old version for you.")
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
        TrustCertificateCommand = new RelayCommand(TrustCertificate, () => CanTrustCertificate);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => CanInstall, OnError);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, () => CanUninstall, OnError);
        RefreshInstallStatus();
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

    public AsyncRelayCommand InstallCommand { get; }

    public RelayCommand TrustCertificateCommand { get; }

    public string CertificateText
    {
        get => _certificateText;
        private set => SetProperty(ref _certificateText, value);
    }

    public bool CanTrustCertificate => _certificateStatus is { Available: true, Trusted: false };

    public AsyncRelayCommand UninstallCommand { get; }

    public bool InstallerAvailable => _ctx.Installer is not null;

    public string InstallStatusText
    {
        get => _installStatusText;
        private set => SetProperty(ref _installStatusText, value);
    }

    public string InstallButtonText
    {
        get => _installButtonText;
        private set => SetProperty(ref _installButtonText, value);
    }

    public bool CanInstall => _installStatus is { RunningFromInstallFolder: false };

    public bool CanUninstall => _installStatus is { IsInstalled: true };

    public string VersionText => $"Clenzy {_ctx.VersionText}";

    public string PrivilegeText => _ctx.Elevation.IsElevated ? "Running as administrator" : "Running with standard user rights";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    private void RefreshInstallStatus()
    {
        _installStatus = null;
        if (_ctx.Installer is null)
        {
            InstallStatusText = string.Empty;
        }
        else
        {
            try
            {
                var status = _ctx.Installer.GetStatus();
                _installStatus = status;
                if (status.IsInstalled)
                {
                    InstallStatusText = status.RunningFromInstallFolder
                        ? $"Installed (version {status.InstalledVersion}) in {status.InstallFolder}"
                        : $"Version {status.InstalledVersion} is installed. This copy is not installed, so installing replaces the old version.";
                    InstallButtonText = status.RunningFromInstallFolder ? "Installed" : "Replace installed version";
                }
                else
                {
                    InstallStatusText = "Clenzy is not installed. Installing adds a Start menu entry and lets you uninstall it from Windows Settings.";
                    InstallButtonText = "Install";
                }
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("Could not read install status", ex);
                InstallStatusText = "The install status could not be read.";
            }
        }

        RefreshCertificate();
        InstallCommand.RaiseCanExecuteChanged();
        UninstallCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanUninstall));
    }

    private void RefreshCertificate()
    {
        _certificateStatus = null;
        CertificateText = string.Empty;
        if (_ctx.Installer is not null)
        {
            try
            {
                var status = _ctx.Installer.GetCertificateStatus();
                _certificateStatus = status;
                CertificateText = !status.Available
                    ? "Signing certificate: this copy is not signed with a Clenzy certificate."
                    : status.Trusted
                        ? $"Signing certificate trusted on this PC ({status.Description})."
                        : $"Signing certificate not trusted yet ({status.Description}). Windows shows an unknown publisher until it is.";
            }
            catch (Exception ex)
            {
                _ctx.Log.Warning("Could not read the certificate status: " + ex.Message);
            }
        }

        TrustCertificateCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanTrustCertificate));
    }

    private void TrustCertificate()
    {
        if (_ctx.Installer is null)
        {
            return;
        }

        Message = _ctx.Installer.TrustCertificate().Message;
        RefreshCertificate();
    }

    private async Task InstallAsync()
    {
        if (_ctx.Installer is null || _isBusy())
        {
            return;
        }

        var replacing = _installStatus is { IsInstalled: true };
        var ok = await _ctx.Dialogs.ConfirmAsync(
            replacing ? "Replace the installed version?" : "Install Clenzy?",
            replacing
                ? "The installed Clenzy is closed and replaced by this version. Your settings, history and backups are kept. Clenzy then restarts from the installed copy. If this build is signed, its signing certificate is trusted on this PC (Windows asks you to confirm)."
                : "Clenzy is copied to your user profile (no administrator rights needed) and added to the Start menu and to Apps & features. If this build is signed, its signing certificate is trusted on this PC (Windows asks you to confirm). Clenzy then restarts from the installed copy.",
            replacing ? "Replace" : "Install").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        var result = _ctx.Installer.InstallThisCopy(trustCertificate: true);
        Message = result.Message;
        if (result.Success && result.ShouldExit)
        {
            _ctx.Lifecycle.Shutdown();
        }

        RefreshInstallStatus();
    }

    private async Task UninstallAsync()
    {
        if (_ctx.Installer is null || _isBusy())
        {
            return;
        }

        var ok = await _ctx.Dialogs.ConfirmAsync(
            "Uninstall Clenzy?",
            "Clenzy closes and its program files, Start menu entry and startup entry are removed. You are asked next whether to keep your settings and history.",
            "Uninstall").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        var keep = await _ctx.Dialogs.ConfirmAsync(
            "Keep your settings and history?",
            "Registry backups are stored with them, so keeping them lets you undo an earlier registry repair later. Choosing \"Delete everything\" also removes Clenzy's trusted signing certificate from this PC.",
            "Keep my data", "Delete everything").ConfigureAwait(true);

        var result = _ctx.Installer.Uninstall(removeUserData: !keep);
        Message = result.Message;
        if (result.Success && result.ShouldExit)
        {
            _ctx.Lifecycle.Shutdown();
        }

        RefreshInstallStatus();
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
        SettingToggleViewModel T(string key, string label, string description, Func<ClenzySettings, bool> get, Action<ClenzySettings, bool> set,
            string? consentTitle = null, string? consentMessage = null, string? badge = null) =>
            new(key, label, description, get, (self, value) => ApplyAsync(self, value, set), consentTitle, consentMessage, badge);

        return new List<SettingsSectionViewModel>
        {
            new("GENERAL", null, new[]
            {
                T("follow-theme", "Follow the Windows light/dark theme", "Turn off to always use the light theme.", s => s.FollowSystemTheme, (s, v) => s.FollowSystemTheme = v),
                T("startup", "Start Clenzy when I sign in", "Adds a per-user startup entry. Clenzy never cleans automatically at startup.", s => s.LaunchOnStartup, (s, v) => s.LaunchOnStartup = v),
                T("tray", "Minimize to the notification area", "Keeps Clenzy in the tray when the window is minimized.", s => s.MinimizeToTray, (s, v) => s.MinimizeToTray = v),
                T("notify", "Show a notification when cleaning finishes", "Only shown when the window is not in front.", s => s.ShowCompletionNotifications, (s, v) => s.ShowCompletionNotifications = v)
            }),
            new("AUTOMATION", "Sign in, and Clenzy opens, cleans on its own, shows what it recovered and closes. Only categories that are safe to run unattended are cleaned.", new[]
            {
                T("auto-clean", "Clean automatically when I sign in", "Opens a window, waits 5 seconds (you can cancel), scans, cleans and then closes itself. Also turns on starting with Windows.",
                    s => s.AutoCleanOnStartup, (s, v) => s.AutoCleanOnStartup = v,
                    "Clean automatically at sign-in?", "Every time you sign in, Clenzy will open, wait 5 seconds, then permanently delete the categories you have enabled that are safe to run unattended (temp files, Prefetch, caches, and Chrome and Edge caches, closing those browsers if they are open). Your sign-ins, passwords and bookmarks are not touched. The Recycle Bin, privacy items, website storage and registry fixes are never done automatically. You can cancel during the countdown.", "Permanent"),
                T("auto-ram", "Free up RAM after an automatic clean", "Once the clean finishes, memory held by idle programs is released. Nothing is closed.", s => s.FreeRamAfterAutoClean, (s, v) => s.FreeRamAfterAutoClean = v),
                T("auto-close", "Close Clenzy after an automatic clean", "Shows the result for about 12 seconds, then closes. Off keeps the window open.", s => s.CloseAfterAutoClean, (s, v) => s.CloseAfterAutoClean = v),
                T("admin-start", "Start with administrator rights (no prompt)", "Uses a Windows scheduled task so Windows Temp, Prefetch and Windows logs are cleaned at sign-in too. Turned on by itself the next time Clenzy runs as administrator while automatic cleaning is on.", s => s.ElevatedStartup, (s, v) => s.ElevatedStartup = v,
                    "Start with administrator rights?", "Clenzy will be started by a Windows scheduled task named \"Clenzy\" that runs with administrator rights at every sign-in, without a UAC prompt. Anything that runs Clenzy.exe from the install folder then runs elevated, so keep that folder protected. You can turn this off here at any time.", "Administrator")
            }),
            new("CLEANING BEHAVIOR", "One-click cleaning only runs categories that are safe to run unattended.", new[]
            {
                T("review", "Always review before cleaning", "Shows every category and lets you deselect items before anything is deleted.", s => s.ReviewBeforeCleaning, (s, v) => s.ReviewBeforeCleaning = v)
            }),
            new("SYSTEM JUNK", "Every item is permanently deleted. Only files older than a minimum age and inside approved folders are touched.", new[]
            {
                T("user-temp", "User temp folder (%TEMP%)", "Files in your temp folder that are not in use.", s => s.CleanUserTemp, (s, v) => s.CleanUserTemp = v),
                T("win-temp", "Windows temp folder", "C:\\Windows\\Temp. Files in use are skipped. Requires administrator.", s => s.CleanWindowsTemp, (s, v) => s.CleanWindowsTemp = v, badge: "Administrator"),
                T("recent-temp", "Include files created in the last few hours", "Off keeps recent temp files so a running installer is not disturbed. Files that are open are always skipped.", s => s.CleanRecentTempFiles, (s, v) => s.CleanRecentTempFiles = v),
                T("prefetch", "Prefetch files", "C:\\Windows\\Prefetch trace files. Windows rebuilds them; the next launch of an app may be a little slower. Requires administrator.", s => s.CleanPrefetch, (s, v) => s.CleanPrefetch = v, badge: "Administrator"),
                T("thumbs", "Thumbnail cache", "Explorer rebuilds thumbnails on demand.", s => s.CleanThumbnailCache, (s, v) => s.CleanThumbnailCache = v),
                T("shader", "DirectX shader cache", "Games and apps rebuild it; the first launch afterward may be slower.", s => s.CleanShaderCache, (s, v) => s.CleanShaderCache = v),
                T("wer", "Error reports", "Windows Error Reporting archives and queues.", s => s.CleanErrorReports, (s, v) => s.CleanErrorReports = v),
                T("dumps", "Crash dumps", "Memory dumps from crashed applications.", s => s.CleanCrashDumps, (s, v) => s.CleanCrashDumps = v),
                T("logs", "Old Windows logs", "Rotated servicing and update logs older than 30 days. Requires administrator.", s => s.CleanWindowsLogs, (s, v) => s.CleanWindowsLogs = v, badge: "Administrator"),
                T("recycle", "Empty the Recycle Bin", "Permanently deletes everything in the Recycle Bin.", s => s.EmptyRecycleBin, (s, v) => s.EmptyRecycleBin = v,
                    "Empty the Recycle Bin?", "When cleaning, Clenzy will permanently empty the Recycle Bin after you confirm in the review screen. Deleted items cannot be recovered.", "Permanent"),
                T("wu", "Windows Update cleanup", "Runs the supported Windows component cleanup (DISM). Requires administrator and can take several minutes.", s => s.CleanWindowsUpdateComponents, (s, v) => s.CleanWindowsUpdateComponents = v,
                    "Enable Windows Update cleanup?", "Clenzy will run the supported command \"dism.exe /Online /Cleanup-Image /StartComponentCleanup\" after you confirm in the review screen. Superseded update components can no longer be uninstalled afterward.", "Administrator"),
                T("do", "Delivery Optimization cache", "Clears downloaded update content shared with other PCs. Requires administrator.", s => s.CleanDeliveryOptimization, (s, v) => s.CleanDeliveryOptimization = v,
                    "Enable Delivery Optimization cleanup?", "Clenzy will ask Windows to delete its Delivery Optimization cache after you confirm in the review screen.", "Administrator")
            }),
            new("BROWSERS", "Chrome and Edge are closed first, then only their cache is cleaned. Passwords, bookmarks, history and cookies are never touched, so you stay signed in.", new[]
            {
                T("close-browsers", "Close running browsers before cleaning", "Closes the window politely and ends leftovers after a few seconds. Off skips any browser that is open.", s => s.CloseBrowsersBeforeCleaning, (s, v) => s.CloseBrowsersBeforeCleaning = v),
                T("ask-browsers", "Ask before closing a browser", "Shows a confirmation naming the browser. Off closes it without asking.", s => s.AskBeforeClosingBrowsers, (s, v) => s.AskBeforeClosingBrowsers = v),
                T("chrome", "Chrome cache", "HTTP, code and GPU caches of every profile.", s => s.CleanChromeCache, (s, v) => s.CleanChromeCache = v),
                T("edge", "Edge cache", "HTTP, code and GPU caches of every profile.", s => s.CleanEdgeCache, (s, v) => s.CleanEdgeCache = v),
                T("site-storage", "Website storage (Local Storage, IndexedDB, Cache Storage)", "Can sign you out of some sites and removes offline site data. Off by default.", s => s.ClearBrowserSiteStorage, (s, v) => s.ClearBrowserSiteStorage = v,
                    "Clear website storage?", "Website storage includes Local Storage, IndexedDB, Session Storage and Cache Storage. Some sites may sign you out or lose offline data. Cookies are not removed. This is confirmed again in the review screen.", "Confirmation")
            }),
            new("PRIVACY", "Privacy cleanup is off by default and always shown in the review screen. Privacy entries are not backed up.", new[]
            {
                T("privacy", "Enable privacy cleanup", "Turns on the options below.", s => s.ClearActivityHistory, (s, v) => s.ClearActivityHistory = v,
                    "Enable privacy cleanup?", "This clears Run history, typed Explorer paths and the Recent items list. These entries are not backed up and cannot be restored.", "Permanent"),
                T("run-history", "Run dialog history", "Clears the Win+R list.", s => s.ClearRunHistory, (s, v) => s.ClearRunHistory = v),
                T("recent", "Recent items", "Removes shortcuts from the Recent folder and the recent documents list.", s => s.ClearRecentItems, (s, v) => s.ClearRecentItems = v),
                T("explorer", "Explorer address bar history", "Clears typed paths.", s => s.ClearExplorerAddressHistory, (s, v) => s.ClearExplorerAddressHistory = v)
            }),
            new("REGISTRY", "The registry is never changed during one-click cleaning.", new[]
            {
                T("registry-scan", "Scan the registry for broken entries", "Read-only. Repairs always need your approval on the registry review page.", s => s.ScanRegistry, (s, v) => s.ScanRegistry = v),
                T("restore-point", "Create a restore point before repairs", "Skipped automatically when System Protection is off or Windows declines.", s => s.CreateRestorePoint, (s, v) => s.CreateRestorePoint = v)
            })
        };
    }

    private async Task<bool> ApplyAsync(SettingToggleViewModel toggle, bool value, Action<ClenzySettings, bool> set)
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

        if (toggle.Key == "startup")
        {
            if (!ApplyStartup(value))
            {
                settings.LaunchOnStartup = previousStartup;
                return false;
            }

            if (!value && settings.AutoCleanOnStartup)
            {
                settings.AutoCleanOnStartup = false;
                Toggle("auto-clean").Refresh();
            }
        }
        else if (toggle.Key == "auto-clean" && value && !settings.LaunchOnStartup)
        {
            settings.LaunchOnStartup = true;
            if (!ApplyStartup(true))
            {
                settings.LaunchOnStartup = previousStartup;
                settings.AutoCleanOnStartup = false;
                return false;
            }

            Toggle("startup").Refresh();
        }
        else if (toggle.Key == "admin-start" && !ApplyElevatedStartup(value, previousStartup))
        {
            settings.ElevatedStartup = !value;
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

                if (_ctx.Settings.Current.ElevatedStartup)
                {
                    return true;
                }

                _ctx.Startup.Register(path);
            }
            else
            {
                _ctx.Startup.Unregister();
                if (_ctx.Settings.Current.ElevatedStartup && !RemoveStartupTask())
                {
                    return false;
                }
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

    private bool RemoveStartupTask()
    {
        if (_ctx.StartupTask is null)
        {
            return true;
        }

        try
        {
            _ctx.StartupTask.Unregister();
            _ctx.Settings.Current.ElevatedStartup = false;
            Toggle("admin-start").Refresh();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            _ctx.Log.Warning("Could not remove the startup task: " + ex.Message);
            Message = "The administrator startup task could not be removed. Run Clenzy as administrator and turn it off, or delete the \"Clenzy\" task in Task Scheduler.";
            return false;
        }
    }

    /// <summary>Switches between the normal per-user startup entry and the elevated scheduled task.</summary>
    private bool ApplyElevatedStartup(bool enable, bool previousStartup)
    {
        var task = _ctx.StartupTask;
        if (task is null)
        {
            Message = "The administrator startup task is not available here.";
            return false;
        }

        if (!_ctx.Elevation.IsElevated)
        {
            Message = "Turning this on or off needs administrator rights. Use \"Run as administrator\" on the home screen first.";
            return false;
        }

        var settings = _ctx.Settings.Current;
        try
        {
            if (enable)
            {
                var path = _ctx.Lifecycle.ExecutablePath;
                if (string.IsNullOrEmpty(path))
                {
                    Message = "The program path is unknown, so the task was not created.";
                    return false;
                }

                task.Register(path);
                _ctx.Startup.Unregister();
                settings.LaunchOnStartup = true;
                Toggle("startup").Refresh();
            }
            else
            {
                task.Unregister();
                if (settings.LaunchOnStartup && _ctx.Lifecycle.ExecutablePath is { Length: > 0 } path)
                {
                    _ctx.Startup.Register(path);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            _ctx.Log.Error("Could not change the administrator startup task", ex);
            Message = "Windows refused the task change: " + ex.Message;
            settings.LaunchOnStartup = previousStartup;
            return false;
        }
    }

    private void AddExcludedFolder()
    {
        var path = _ctx.Dialogs.PickFolder("Choose a folder Clenzy must never clean");
        AddExclusion(path, folder: true);
    }

    private void AddExcludedFile()
    {
        var path = _ctx.Dialogs.PickFile("Choose a file Clenzy must never delete");
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

        var path = _ctx.Dialogs.PickSaveFile($"Clenzy-report-{_ctx.Time.GetLocalNow():yyyyMMdd-HHmm}.txt", "Text report (*.txt)|*.txt");
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
            "Clear Clenzy history?",
            "This deletes Clenzy's own session history and log files. It does not touch registry backups or anything outside Clenzy's data folder.",
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
        var hadTask = _ctx.Settings.Current.ElevatedStartup;
        _ctx.Settings.Reset();
        if (hadStartup)
        {
            ApplyStartup(false);
        }

        if (hadTask && _ctx.StartupTask is not null)
        {
            try
            {
                _ctx.StartupTask.Unregister();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or IOException)
            {
                _ctx.Log.Warning("Could not remove the startup task during reset: " + ex.Message);
            }
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
