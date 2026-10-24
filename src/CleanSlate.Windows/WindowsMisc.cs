using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using CleanSlate.Core.Interfaces;
using CleanSlate.Presentation.Infrastructure;
using Microsoft.Win32;

namespace CleanSlate.Windows;

public sealed class WindowsFileProbe : IFileProbe
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool IsOnFixedLocalDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return false;
            }

            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Fixed && drive.IsReady;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class WindowsEnvironmentExpander : IEnvironmentExpander
{
    public string Expand(string value) => Environment.ExpandEnvironmentVariables(value);

    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}

/// <summary>Reports whether this process is elevated and, only on explicit request, starts an elevated copy after UAC.</summary>
public sealed class ElevationService : IPrivilegeInfo, IElevationService
{
    private readonly Lazy<bool> _elevated = new(Detect);

    public bool IsElevated => _elevated.Value;

    public ElevationRestartResult RestartElevated()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return ElevationRestartResult.Failed;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "--restarted")
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            });
            return process is null ? ElevationRestartResult.Failed : ElevationRestartResult.Started;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == NativeMethods.ErrorCancelledByUser)
        {
            return ElevationRestartResult.Declined;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ElevationRestartResult.Failed;
        }
    }

    private static bool Detect()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>Per-user "start when I sign in" entry. Writes only HKCU\...\Run\CleanSlate.</summary>
public sealed class StartupRegistration : IStartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CleanSlate";

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunPath, false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void Register(string executablePath)
    {
        if (!Path.IsPathRooted(executablePath) || executablePath.Contains('"'))
        {
            throw new ArgumentException("The executable path is not valid.", nameof(executablePath));
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunPath, writable: true);
        key.SetValue(ValueName, $"\"{executablePath}\" --startup", RegistryValueKind.String);
    }

    public void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

public static class DwmInterop
{
    /// <summary>Sets a DWM window attribute (for example the dark title bar). Returns the HRESULT.</summary>
    public static int SetAttribute(IntPtr hwnd, int attribute, int value) =>
        NativeMethods.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
}
