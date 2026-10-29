using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Win32;
namespace Clenzy.Windows;

public static class LegacyInstallation
{
    public static bool MatchesCommand(string? command, string executable) => command is not null &&
        (command.Trim().Equals(executable,StringComparison.OrdinalIgnoreCase) || command.Trim().StartsWith("\""+executable+"\"",StringComparison.OrdinalIgnoreCase));
    public static void Retire()
    {
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder=Path.Combine(local,"Programs","CleanSlate");
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Legacy install folder is a link; automatic migration stopped.");
        var executable=Path.Combine(folder,"CleanSlate.exe");
        if (string.Equals(Environment.ProcessPath,executable,StringComparison.OrdinalIgnoreCase)) return;
        var platform=new WindowsInstallPlatform(Clenzy.Core.Configuration.AppDataLayout.ForCurrentUser());
        if (File.Exists(executable)) {
            var info=FileVersionInfo.GetVersionInfo(executable);
            if (!(info.ProductName?.Contains("CleanSlate",StringComparison.OrdinalIgnoreCase) == true)) return;
        }
        // Remove only the legacy app's own startup registration, never unrelated Run values.
        using(var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)) {
            if (MatchesCommand(run?.GetValue("CleanSlate") as string,executable)) run!.DeleteValue("CleanSlate",false);
        }
        var task=RunTask("/Query","/TN","CleanSlate","/XML");
        if(task.Code==0) {
            var xml=XDocument.Parse(task.Output);
            var commands=xml.Descendants().Where(x=>x.Name.LocalName=="Command").Select(x=>x.Value).ToArray();
            if(commands.Length==1 && MatchesCommand(commands[0],executable)) {
                var result=RunTask("/Delete","/TN","CleanSlate","/F");
                if(result.Code!=0) throw new IOException("Could not remove the old CleanSlate startup task: "+result.Output);
            }
        }
        if (File.Exists(executable) && platform.StopOtherInstances(executable)>0) throw new IOException("Close the old CleanSlate app before completing migration.");
        var shortcut=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"CleanSlate.lnk");
        if(File.Exists(shortcut)) {
            var type=Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell=Activator.CreateInstance(type)!;
            dynamic link=shell.CreateShortcut(shortcut);
            try { if(string.Equals((string)link.TargetPath,executable,StringComparison.OrdinalIgnoreCase)) File.Delete(shortcut); }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        using(var uninstall=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall",true)) {
            using var old=uninstall?.OpenSubKey("CleanSlate");
            var location=old?.GetValue("InstallLocation") as string;
            if(string.Equals(location,folder,StringComparison.OrdinalIgnoreCase)) { old!.Close(); uninstall!.DeleteSubKeyTree("CleanSlate",false); }
        }
        if(File.Exists(executable)) File.Delete(executable);
        // Keep settings, cleaning history and recovery backups in the legacy data directory.
    }
    private static (int Code,string Output) RunTask(params string[] args)
    {
        var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"schtasks.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args) info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        if(!process.WaitForExit(15000)) {process.Kill(true); throw new IOException("Old startup-task migration timed out.");}
        return (process.ExitCode,output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult());
    }
}
