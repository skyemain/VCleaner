using System.Xml.Linq;
using Clenzy.Windows.Windows11;
using Xunit;

namespace Clenzy.WindowsTests;

public sealed class Windows11SandboxTests
{
    [Fact] public void Sandbox_maps_only_staged_executable_folder_readonly_and_disables_external_channels()
    {
        var xml = XDocument.Parse(WindowsSandboxLauncher.Configuration(Path.Combine(Path.GetTempPath(), "Clenzy & Test"), "Clenzy.exe"));
        Assert.Equal("Disable", xml.Root!.Element("Networking")!.Value);
        Assert.Equal("Disable", xml.Root.Element("ClipboardRedirection")!.Value);
        var map = Assert.Single(xml.Descendants("MappedFolder"));
        Assert.Equal("true", map.Element("ReadOnly")!.Value);
        Assert.Equal(@"C:\ClenzyReadOnly", map.Element("SandboxFolder")!.Value);
        Assert.Contains("--windows11-cleanup", xml.Descendants("Command").Single().Value);
    }
    [Theory] [InlineData("../bad.exe")] [InlineData("C:\\bad.exe")] [InlineData("bad.ps1")]
    public void Sandbox_rejects_non_filename_or_non_executable(string name) => Assert.Throws<ArgumentException>(() => WindowsSandboxLauncher.Configuration(Path.GetTempPath(), name));
}
