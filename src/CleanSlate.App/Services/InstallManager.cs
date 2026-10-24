using System.Diagnostics;
using System.IO;
using CleanSlate.Core.Install;
using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.App.Services;

/// <summary>Connects the install service to the running app: installs this copy, restarts from the installed copy, or uninstalls.</summary>
public sealed class InstallManager : IInstallManager
{
    private readonly InstallService _service;
    private readonly string _versionText;

    public InstallManager(InstallService service, string versionText)
    {
        _service = service;
        _versionText = versionText;
    }

    public static string? CurrentExecutable => Environment.ProcessPath;

    public InstallStatus GetStatus()
    {
        var installed = _service.GetInstalled();
        var running = _service.IsRunningFromInstallFolder(CurrentExecutable);
        return new InstallStatus(installed is not null, installed?.Version, running, installed?.Directory ?? Path.GetDirectoryName(_service.InstalledExecutablePath) ?? string.Empty);
    }

    public InstallActionResult InstallThisCopy()
    {
        var source = CurrentExecutable;
        if (string.IsNullOrEmpty(source))
        {
            return new InstallActionResult(false, "The program path is unknown, so nothing was installed.", false);
        }

        try
        {
            var result = _service.Install(source, _versionText);
            if (result.Kind == InstallResultKind.AlreadyRunningFromInstallFolder)
            {
                return new InstallActionResult(true, "This copy is already the installed one.", false);
            }

            Launch(result.ExecutablePath);
            var verb = result.Kind == InstallResultKind.Updated ? $"Replaced version {result.PreviousVersion}." : "Installed.";
            return new InstallActionResult(true, verb + " Restarting from the installed copy...", true);
        }
        catch (InstallException ex)
        {
            return new InstallActionResult(false, ex.Message, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new InstallActionResult(false, "Installing failed: " + ex.Message, false);
        }
    }

    public InstallActionResult Uninstall(bool removeUserData)
    {
        try
        {
            var result = _service.Uninstall(removeUserData);
            if (!result.WasInstalled)
            {
                return new InstallActionResult(true, "CleanSlate was not installed.", false);
            }

            var exit = removeUserData || _service.IsRunningFromInstallFolder(CurrentExecutable);
            return new InstallActionResult(true, removeUserData ? "CleanSlate and its data are being removed." : "CleanSlate is being removed. Your settings were kept.", exit);
        }
        catch (InstallException ex)
        {
            return new InstallActionResult(false, ex.Message, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new InstallActionResult(false, "Uninstalling failed: " + ex.Message, false);
        }
    }

    public static void Launch(string executable)
    {
        Process.Start(new ProcessStartInfo(executable, "--restarted")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
        })?.Dispose();
    }
}
