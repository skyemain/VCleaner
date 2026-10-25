using System.Diagnostics;
using System.Runtime.InteropServices;
using Clenzy.Core.Configuration;
using Clenzy.Core.Install;

namespace Clenzy.Windows;

/// <summary>Per-user install location under %LOCALAPPDATA%\Programs. Nothing here needs administrator rights.</summary>
public sealed class WindowsInstallPlatform : IInstallPlatform
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    public WindowsInstallPlatform(AppDataLayout layout)
    {
        InstallDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", InstallService.ProductName);
        UserDataDirectory = layout.Root;
        StartMenuShortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), InstallService.ProductName + ".lnk");
    }

    public string InstallDirectory { get; }

    public string UserDataDirectory { get; }

    public string StartMenuShortcutPath { get; }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string source, string destination) => File.Copy(source, destination, overwrite: true);

    /// <summary>
    /// Deletes the Zone.Identifier stream (Mark of the Web) that <see cref="File.Copy(string, string, bool)"/> carries over from a
    /// downloaded exe. Without this, SmartScreen treats the installed copy as an unapproved download on every Start menu or
    /// sign-in launch, even after the user already approved the download itself.
    /// </summary>
    public void RemoveDownloadMarker(string path)
    {
        try
        {
            File.Delete(path + ":Zone.Identifier");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The marker was absent or locked; the copied file itself is fine either way.
        }
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    public bool IsSameFile(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public int StopOtherInstances(string executablePath)
    {
        var self = Environment.ProcessId;
        var remaining = 0;
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            return 0;
        }

        foreach (var process in candidates)
        {
            try
            {
                if (process.Id == self || !SamePath(process, executablePath))
                {
                    continue;
                }

                try
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(CloseGrace))
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Already exited, or not ours to end.
                }

                if (!process.HasExited)
                {
                    remaining++;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Cannot be inspected (for example elevated); treated as not ours.
            }
            finally
            {
                process.Dispose();
            }
        }

        return remaining;
    }

    public void CreateShortcut(string shortcutPath, string targetPath, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new InvalidOperationException("The Windows shortcut service is not available.");
        object? shell = null;
        object? link = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic wsh = shell!;
            link = wsh.CreateShortcut(shortcutPath);
            dynamic shortcut = link!;
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            shortcut.Description = description;
            shortcut.IconLocation = targetPath + ",0";
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            shortcut.Save();
        }
        finally
        {
            if (link is not null)
            {
                Marshal.FinalReleaseComObject(link);
            }

            if (shell is not null)
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    public void ScheduleDeleteAfterExit(string directory)
    {
        if (directory.Contains('"') || directory.Contains('%') || directory.Contains('&'))
        {
            throw new InvalidOperationException("The folder path cannot be scheduled for removal.");
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var arguments = $"/d /c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{directory}\"";
        Process.Start(new ProcessStartInfo(Path.Combine(system, "cmd.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = system
        })?.Dispose();
    }

    private static bool SamePath(Process process, string executablePath)
    {
        var path = process.MainModule?.FileName;
        return path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(executablePath), StringComparison.OrdinalIgnoreCase);
    }
}
