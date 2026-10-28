using Clenzy.Core.Models;
using Clenzy.Presentation.ViewModels;
using Xunit;

namespace Clenzy.UnitTests;

public sealed class CompletionPresentationTests
{
    [Theory]
    [InlineData(0, RowKind.Success)]
    [InlineData(1, RowKind.Error)]
    public void Routine_skips_do_not_warn_but_errors_still_do(int errors, RowKind expected)
    {
        var scan = new ScanReport { Modules = new[] {
            ModuleScanResult.Empty("temp", "Temp", CleanerGroup.SystemJunk, ModuleScanStatus.Completed, null)
        }};
        var clean = new CleanReport { Modules = new[] {
            new ModuleCleanResult { ModuleId = "temp", ModuleName = "Temp", Group = CleanerGroup.SystemJunk,
                ItemsCleaned = 2, ItemsSkipped = 9, BytesRecovered = 1024, Errors = errors }
        }};
        var row = GroupStatusFormatter.Describe(CleanerGroup.SystemJunk, scan, clean);
        Assert.DoesNotContain("skipped", row.Text);
        Assert.Equal(expected, row.Kind);
        Assert.Equal(9, clean.ItemsSkipped);
        Assert.Equal(errors > 0, row.Text.Contains("error"));
    }
}
