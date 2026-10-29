using Clenzy.Windows;
using Xunit;
namespace Clenzy.WindowsTests;
public sealed class LegacyMigrationTests
{
    [Theory]
    [InlineData("\"C:\\Users\\Test\\CleanSlate.exe\" --startup",true)]
    [InlineData("C:\\Users\\Test\\CleanSlate.exe",true)]
    [InlineData("\"C:\\Other\\CleanSlate.exe\" --startup",false)]
    [InlineData("\"C:\\Users\\Test\\CleanSlate.exe.other\"",false)]
    public void Legacy_startup_matching_is_limited_to_expected_executable(string command,bool expected)
        => Assert.Equal(expected,LegacyInstallation.MatchesCommand(command,@"C:\Users\Test\CleanSlate.exe"));
}
