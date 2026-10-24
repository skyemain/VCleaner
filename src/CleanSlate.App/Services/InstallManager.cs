using System.Diagnostics;
using System.IO;
using CleanSlate.Core.Install;
using CleanSlate.Core.Interfaces;
using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.App.Services;

/// <summary>Connects the install service to the running app: installs this copy, restarts from the installed copy, or uninstalls.</summary>
public sealed class InstallManager : IInstallManager
{
    private readonly InstallService _service;
    private readonly string _versionText;
    private readonly ICertificateTrust? _certificate;

    public InstallManager(InstallService service, string versionText, ICertificateTrust? certificate = null)
    {
        _service = service;
        _versionText = versionText;
        _certificate = certificate;
    }

    public static string? CurrentExecutable => Environment.ProcessPath;

    public InstallStatus GetStatus()
    {
        var installed = _service.GetInstalled();
        var running = _service.IsRunningFromInstallFolder(CurrentExecutable);
        return new InstallStatus(installed is not null, installed?.Version, running, installed?.Directory ?? Path.GetDirectoryName(_service.InstalledExecutablePath) ?? string.Empty);
    }

    public InstallActionResult InstallThisCopy(bool trustCertificate)
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

            var trustNote = trustCertificate ? TryTrust() : string.Empty;
            Launch(result.ExecutablePath);
            var verb = result.Kind == InstallResultKind.Updated ? $"Replaced version {result.PreviousVersion}." : "Installed.";
            return new InstallActionResult(true, verb + trustNote + " Restarting from the installed copy...", true);
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

    public CertificateStatus GetCertificateStatus() =>
        _certificate is null
            ? new CertificateStatus(false, false, string.Empty)
            : new CertificateStatus(_certificate.IsAvailable, _certificate.IsAvailable && _certificate.IsTrusted, _certificate.Description);

    public InstallActionResult TrustCertificate()
    {
        if (_certificate is not { IsAvailable: true })
        {
            return new InstallActionResult(false, "This copy is not signed with a CleanSlate certificate, so there is nothing to trust.", false);
        }

        try
        {
            _certificate.Trust();
            return _certificate.IsTrusted
                ? new InstallActionResult(true, "The CleanSlate signing certificate is now trusted on this PC.", false)
                : new InstallActionResult(false, "Windows did not add the certificate (the confirmation may have been declined).", false);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new InstallActionResult(false, "The certificate could not be trusted: " + ex.Message, false);
        }
    }

    private string TryTrust()
    {
        if (_certificate is not { IsAvailable: true } || _certificate.IsTrusted)
        {
            return string.Empty;
        }

        var result = TrustCertificate();
        return result.Success ? " The signing certificate is trusted." : " " + result.Message;
    }

    public InstallActionResult Uninstall(bool removeUserData)
    {
        try
        {
            if (removeUserData && _certificate is { IsAvailable: true })
            {
                _certificate.Remove();
            }

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
