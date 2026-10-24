using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;

namespace CleanSlate.Presentation.Infrastructure;

public interface IDialogService
{
    /// <summary>Asks the user to confirm. Returns true only for an explicit "yes".</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Cancel");

    Task ShowMessageAsync(string title, string message);

    /// <summary>Returns the chosen path or null when canceled.</summary>
    string? PickSaveFile(string suggestedName, string filter);

    string? PickFolder(string title);
    string? PickFile(string title);
    void OpenFolder(string path);
}

public interface INotificationService
{
    void NotifyCompleted(string title, string message);
}

public enum ElevationRestartResult
{
    Started,
    Declined,
    Failed
}

public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>Starts a new elevated instance after the Windows UAC prompt. Never bypasses UAC.</summary>
    ElevationRestartResult RestartElevated();
}

public interface IAppLifecycle
{
    string? ExecutablePath { get; }

    void Shutdown();
}

public sealed record InstallStatus(bool IsInstalled, string? InstalledVersion, bool RunningFromInstallFolder, string InstallFolder);

public sealed record CertificateStatus(bool Available, bool Trusted, string Description);

public sealed record InstallActionResult(bool Success, string Message, bool ShouldExit);

/// <summary>The per-user installer: put this copy into its install folder (replacing any older one) or remove CleanSlate again.</summary>
public interface IInstallManager
{
    InstallStatus GetStatus();

    /// <summary>Installs the running copy, replacing an older installed version, then starts the installed copy. <c>ShouldExit</c> tells the app to close.</summary>
    InstallActionResult InstallThisCopy(bool trustCertificate);

    CertificateStatus GetCertificateStatus();

    /// <summary>Trusts the self-signed certificate this build is signed with (Windows asks for confirmation).</summary>
    InstallActionResult TrustCertificate();

    /// <summary>Removes the program, its shortcut and its startup entry (and, with <c>removeUserData</c>, its data and certificate trust). <c>ShouldExit</c> is true because the running files are deleted after exit.</summary>
    InstallActionResult Uninstall(bool removeUserData);
}

public interface ILogAccess
{
    string LogsDirectory { get; }
    void ClearLogs();
}

/// <summary>Holds the live settings object and persists changes.</summary>
public sealed class SettingsService
{
    private readonly ISettingsStore _store;

    public SettingsService(ISettingsStore store)
    {
        _store = store;
        Current = store.Load();
    }

    public CleanSlateSettings Current { get; private set; }

    public event EventHandler? Changed;

    public void Save()
    {
        _store.Save(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _store.Reset();
        Current = _store.Load();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
