using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Clenzy.Core.Interfaces;

namespace Clenzy.Windows;

/// <summary>
/// A scheduled task named "Clenzy" that starts the app with <c>--startup</c> when the current user signs in, with the highest
/// privileges available to that user. Creating or deleting it needs an elevated process; Windows enforces that, nothing is bypassed.
/// </summary>
public sealed class WindowsStartupTask : IStartupTask
{
    public const string TaskName = "Clenzy";

    public bool IsRegistered
    {
        get
        {
            var (code, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
            return code == 0;
        }
    }

    public void Register(string executablePath)
    {
        if (!Path.IsPathRooted(executablePath) || executablePath.Contains('"') || executablePath.Contains('<') || executablePath.Contains('>'))
        {
            throw new ArgumentException("The executable path is not valid.", nameof(executablePath));
        }

        using var identity = WindowsIdentity.GetCurrent();
        var xml = BuildXml(identity.Name, executablePath);
        var file = Path.Combine(Path.GetTempPath(), $"clenzy-task-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            var (code, output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F");
            if (code != 0)
            {
                throw new InvalidOperationException("Windows could not create the sign-in task (administrator rights are required): " + output.Trim());
            }
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leftover temp file is harmless.
            }
        }
    }

    public void Unregister()
    {
        if (!IsRegistered)
        {
            return;
        }

        var (code, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        if (code != 0)
        {
            throw new InvalidOperationException("Windows could not remove the sign-in task (administrator rights are required): " + output.Trim());
        }
    }

    public static string BuildXml(string userName, string executablePath)
    {
        var user = SecurityElement.Escape(userName);
        var exe = SecurityElement.Escape(executablePath);
        var folder = SecurityElement.Escape(Path.GetDirectoryName(executablePath) ?? string.Empty);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts Clenzy when you sign in (optional automatic cleaning).</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <Arguments>--startup</Arguments>
                  <WorkingDirectory>{folder}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int ExitCode, string Output) RunSchtasks(string arguments)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var info = new ProcessStartInfo(Path.Combine(system, "schtasks.exe"), arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = system
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("schtasks.exe could not be started.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20_000))
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already exited.
            }

            throw new InvalidOperationException("schtasks.exe did not finish in time.");
        }

        return (process.ExitCode, output);
    }
}
