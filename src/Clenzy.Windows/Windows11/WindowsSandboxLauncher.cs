using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Clenzy.Windows.Windows11;

public static class WindowsSandboxLauncher
{
    public static string Configuration(string sourceFolder, string executableName)
    {
        if (Path.GetFileName(executableName) != executableName || !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Expected an executable filename.");
        return new XElement("Configuration",
            new XElement("Networking", "Disable"), new XElement("vGPU", "Disable"),
            new XElement("AudioInput", "Disable"), new XElement("VideoInput", "Disable"),
            new XElement("PrinterRedirection", "Disable"), new XElement("ClipboardRedirection", "Disable"),
            new XElement("MappedFolders", new XElement("MappedFolder", new XElement("HostFolder", Path.GetFullPath(sourceFolder)),
                new XElement("SandboxFolder", @"C:\ClenzyReadOnly"), new XElement("ReadOnly", "true"))),
            new XElement("LogonCommand", new XElement("Command", $"\"C:\\ClenzyReadOnly\\{executableName}\" --windows11-cleanup"))).ToString();
    }
    public static void Launch(string executable)
    {
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsSandbox.exe");
        if (!File.Exists(launcher)) throw new InvalidOperationException("Windows Sandbox is unavailable. Enable it through Windows Features on a supported edition; Clenzy will not change Windows features automatically. Fixture preview remains available.");
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clenzy", "Windows11", "sandbox-launch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Share only an isolated executable copy, never the original application or user folders.
        var copy = Path.Combine(root, "Clenzy.exe"); File.Copy(executable, copy);
        if (!SHA256.HashData(File.ReadAllBytes(executable)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(copy)))) throw new IOException("Sandbox executable verification failed.");
        var config = Path.Combine(root, "Clenzy.wsb"); File.WriteAllText(config, Configuration(root, "Clenzy.exe"));
        var info = new ProcessStartInfo(launcher) { UseShellExecute = false }; info.ArgumentList.Add(config); Process.Start(info);
    }
}
