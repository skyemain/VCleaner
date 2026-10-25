using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Services;

namespace Clenzy.Core.Install;

/// <summary>Everything the installer needs from the operating system, so the logic can be tested without touching a real PC.</summary>
public interface IInstallPlatform
{
    /// <summary>The per-user install folder, for example <c>%LOCALAPPDATA%\Programs\Clenzy</c>.</summary>
    string InstallDirectory { get; }

    /// <summary>Clenzy's own data folder (settings, logs, history, registry backups).</summary>
    string UserDataDirectory { get; }

    string StartMenuShortcutPath { get; }

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    /// <summary>Copies the file, replacing an existing one.</summary>
    void CopyFile(string source, string destination);

    /// <summary>
    /// Removes the "downloaded from the internet" marker from a file the installer wrote. Copying a downloaded exe carries the
    /// marker along, and Windows then treats every launch of the installed copy as a brand-new unapproved download.
    /// </summary>
    void RemoveDownloadMarker(string path);

    void DeleteFile(string path);

    /// <summary>Deletes a folder and everything in it. Only called for the validated install and data folders.</summary>
    void DeleteDirectory(string path);

    /// <summary>Closes (then ends) other running copies of the given executable. Never the current process. Returns how many could not be stopped.</summary>
    int StopOtherInstances(string executablePath);

    void CreateShortcut(string shortcutPath, string targetPath, string description);

    /// <summary>Removes the folder after this process has exited, for files that cannot be deleted while the app runs.</summary>
    void ScheduleDeleteAfterExit(string directory);

    bool IsSameFile(string a, string b);
}

public sealed record InstalledApp(string Version, string Directory, string ExecutablePath);

public enum InstallResultKind
{
    Installed,
    Updated,
    AlreadyRunningFromInstallFolder
}

public sealed record InstallResult(InstallResultKind Kind, string ExecutablePath, string? PreviousVersion);

public sealed record UninstallResult(bool WasInstalled, bool UserDataRemoved);

public sealed class InstallException : Exception
{
    public InstallException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Per-user installer built into the single executable. Installing a new build replaces the previous installed version;
/// uninstalling removes the program, its shortcut, its startup entry and its "Apps &amp; features" entry. No administrator
/// rights are needed because everything lives under the current user's profile.
/// </summary>
public sealed class InstallService
{
    public const string ProductName = "Clenzy";
    public const string ExecutableName = "Clenzy.exe";
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Clenzy";

    private static readonly RegistryKeyRef UninstallKey = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, UninstallKeyPath);

    private readonly IInstallPlatform _platform;
    private readonly IRegistryAccess _registry;
    private readonly IStartupRegistration _startup;
    private readonly IAppLogger _log;
    private readonly IStartupTask? _startupTask;

    public InstallService(IInstallPlatform platform, IRegistryAccess registry, IStartupRegistration startup, IAppLogger log, IStartupTask? startupTask = null)
    {
        _startupTask = startupTask;
        _platform = platform;
        _registry = registry;
        _startup = startup;
        _log = log;
    }

    public string InstalledExecutablePath => Path.Combine(_platform.InstallDirectory, ExecutableName);

    public bool IsRunningFromInstallFolder(string? executablePath) =>
        !string.IsNullOrEmpty(executablePath) && PathGuard.IsUnder(_platform.InstallDirectory, executablePath);

    /// <summary>The installed copy, or null when Clenzy is not installed (or its files are gone).</summary>
    public InstalledApp? GetInstalled()
    {
        if (!_registry.KeyExists(UninstallKey))
        {
            return null;
        }

        var version = _registry.GetValue(UninstallKey, "DisplayVersion")?.AsText;
        var executable = InstalledExecutablePath;
        if (!_platform.FileExists(executable))
        {
            return null;
        }

        return new InstalledApp(version ?? "unknown", _platform.InstallDirectory, executable);
    }

    public InstallResult Install(string sourceExecutable, string version)
    {
        EnsureSafeInstallDirectory();
        if (string.IsNullOrWhiteSpace(sourceExecutable) || !_platform.FileExists(sourceExecutable) ||
            !sourceExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallException("The program file to install could not be found.");
        }

        var target = InstalledExecutablePath;
        if (_platform.FileExists(target) && _platform.IsSameFile(sourceExecutable, target))
        {
            return new InstallResult(InstallResultKind.AlreadyRunningFromInstallFolder, target, null);
        }

        var previous = GetInstalled();
        if (_platform.FileExists(target))
        {
            if (_platform.StopOtherInstances(target) > 0)
            {
                throw new InstallException("The installed Clenzy is still running and could not be closed. Close it from the Task Manager and try again.");
            }
        }

        try
        {
            _platform.CreateDirectory(_platform.InstallDirectory);
            if (_platform.FileExists(target))
            {
                _platform.DeleteFile(target);
            }

            _platform.CopyFile(sourceExecutable, target);
            _platform.RemoveDownloadMarker(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallException("Clenzy could not be copied into its install folder: " + ex.Message, ex);
        }

        WriteUninstallEntry(target, version);

        try
        {
            _platform.CreateShortcut(_platform.StartMenuShortcutPath, target, "Safe Windows cleanup");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _log.Warning("The Start menu shortcut could not be created: " + ex.Message);
        }

        try
        {
            if (_startup.IsRegistered)
            {
                _startup.Register(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Warning("The startup entry could not be pointed at the new version: " + ex.Message);
        }

        _log.Info(previous is null ? $"Installed Clenzy {version}." : $"Replaced Clenzy {previous.Version} with {version}.");
        return new InstallResult(previous is null ? InstallResultKind.Installed : InstallResultKind.Updated, target, previous?.Version);
    }

    public UninstallResult Uninstall(bool removeUserData)
    {
        EnsureSafeInstallDirectory();
        var installed = GetInstalled();
        var hadRegistration = _registry.KeyExists(UninstallKey);
        if (installed is null && !hadRegistration && !_platform.DirectoryExists(_platform.InstallDirectory))
        {
            return new UninstallResult(false, false);
        }

        if (_platform.FileExists(InstalledExecutablePath) && _platform.StopOtherInstances(InstalledExecutablePath) > 0)
        {
            throw new InstallException("Clenzy is still running and could not be closed. Close it from the Task Manager and try again.");
        }

        try
        {
            _startup.Unregister();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("The startup entry could not be removed: " + ex.Message);
        }

        try
        {
            if (_startupTask is { IsRegistered: true })
            {
                _startupTask.Unregister();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _log.Warning("The elevated startup task could not be removed (administrator rights are needed). Delete the \"Clenzy\" task in Task Scheduler: " + ex.Message);
        }

        TryDeleteFile(_platform.StartMenuShortcutPath);

        if (hadRegistration)
        {
            try
            {
                _registry.DeleteKey(UninstallKey);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _log.Warning("The Apps & features entry could not be removed: " + ex.Message);
            }
        }

        _platform.ScheduleDeleteAfterExit(_platform.InstallDirectory);

        var dataRemoved = false;
        if (removeUserData)
        {
            EnsureSafeUserDataDirectory();
            _platform.ScheduleDeleteAfterExit(_platform.UserDataDirectory);
            dataRemoved = true;
        }

        _log.Info(removeUserData ? "Clenzy and its data are scheduled for removal." : "Clenzy is scheduled for removal; settings and backups were kept.");
        return new UninstallResult(true, dataRemoved);
    }

    private void WriteUninstallEntry(string executable, string version)
    {
        try
        {
            _registry.CreateKey(UninstallKey);
            void Text(string name, string value) => _registry.SetValue(UninstallKey, name, RegistryData.String(value));
            void Number(string name, int value) => _registry.SetValue(UninstallKey, name, new RegistryData(RegistryDataKind.DWord, value));

            Text("DisplayName", ProductName);
            Text("DisplayVersion", version);
            Text("Publisher", ProductName);
            Text("InstallLocation", _platform.InstallDirectory);
            Text("DisplayIcon", executable);
            Text("UninstallString", $"\"{executable}\" --uninstall");
            Text("QuietUninstallString", $"\"{executable}\" --uninstall --quiet");
            Text("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
            Number("NoModify", 1);
            Number("NoRepair", 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InstallException("Clenzy was copied, but Windows refused the Apps & features entry: " + ex.Message, ex);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (_platform.FileExists(path))
            {
                _platform.DeleteFile(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning("Could not remove " + Path.GetFileName(path) + ": " + ex.Message);
        }
    }

    private void EnsureSafeInstallDirectory() => EnsureOwnFolder(_platform.InstallDirectory, "install");

    private void EnsureSafeUserDataDirectory() => EnsureOwnFolder(_platform.UserDataDirectory, "data");

    /// <summary>Recursive deletes are only allowed on a folder that is named after the product and is not a drive root or profile folder.</summary>
    private static void EnsureOwnFolder(string directory, string what)
    {
        if (string.IsNullOrWhiteSpace(directory) ||
            !Path.IsPathRooted(directory) ||
            directory.Contains('"') ||
            !PathGuard.TryCanonicalize(directory, out var canonical, out _) ||
            !string.Equals(Path.GetFileName(PathGuard.TrimTrailingSeparator(canonical)), ProductName, StringComparison.OrdinalIgnoreCase) ||
            Path.GetPathRoot(canonical) is null ||
            canonical.Length <= Path.GetPathRoot(canonical)!.Length + ProductName.Length)
        {
            throw new InstallException($"The {what} folder is not a safe Clenzy folder, so nothing was changed.");
        }
    }
}
