using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.TestSupport;
using Xunit;

namespace CleanSlate.IntegrationTests;

public sealed class EngineIntegrationTests
{
    private static void SeedJunk(Harness h)
    {
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/a.tmp", 1000, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/sub/b.log", 2000, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/fresh.tmp", 50, DateTime.UtcNow);
        h.Temp.CreateFile("Users/tester/Documents/important.docx", 5000, Harness.OldUtc);
    }

    [Fact]
    public async Task Scanning_does_not_modify_the_file_system_or_the_registry()
    {
        using var h = new Harness(TestRegistry.Locations);
        SeedJunk(h);
        h.RegistryProbe.AddFixedDrive("C:");
        h.Registry.Seed(TestRegistry.RunKey, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var before = Harness.SnapshotTree(h.Temp.Path);
        var writesBefore = h.Registry.WriteCount;
        var settings = Harness.Minimal(s =>
        {
            s.CleanUserTemp = true;
            s.ScanRegistry = true;
            s.EmptyRecycleBin = true;
        });

        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);

        Assert.Equal(before, Harness.SnapshotTree(h.Temp.Path));
        Assert.Equal(writesBefore, h.Registry.WriteCount);
        Assert.Equal(0, h.Bin.EmptyCalls);
        Assert.NotEmpty(scan.AllFindings);
    }

    [Fact]
    public async Task Preview_and_execution_use_the_same_eligibility_and_numbers()
    {
        using var h = new Harness();
        SeedJunk(h);
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);

        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        var plan = PlanBuilder.Build(scan, PlanMode.Automatic);
        var clean = await h.Engine.CleanAsync(plan, settings, null, CancellationToken.None);

        Assert.Equal(3000, scan.SelectedBytes);
        Assert.Equal(scan.SelectedBytes, plan.EstimatedBytes);
        Assert.Equal(plan.EstimatedBytes, clean.BytesRecovered);
        Assert.Equal(2, clean.ItemsCleaned);
        Assert.True(File.Exists(h.Temp.Combine("Users/tester/Documents/important.docx")));
        Assert.True(File.Exists(h.Temp.Combine("Users/tester/AppData/Local/Temp/fresh.tmp")));
    }

    [Fact]
    public async Task A_file_that_changes_between_scan_and_clean_is_skipped_not_deleted()
    {
        using var h = new Harness();
        var path = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/changing.tmp", 400, Harness.OldUtc);
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        var plan = PlanBuilder.Build(scan, PlanMode.Automatic);

        File.WriteAllBytes(path, new byte[900]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        var clean = await h.Engine.CleanAsync(plan, settings, null, CancellationToken.None);

        Assert.Equal(0, clean.ItemsCleaned);
        Assert.Equal(1, clean.ItemsSkipped);
        Assert.Equal(0, clean.BytesRecovered);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Every_deletion_is_written_to_the_audit_log()
    {
        using var h = new Harness();
        SeedJunk(h);
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);

        await h.Engine.CleanAsync(PlanBuilder.Build(scan, PlanMode.Automatic), settings, null, CancellationToken.None);

        var audit = h.Log.Lines.Where(l => l.StartsWith("AUDIT", StringComparison.Ordinal)).ToList();
        Assert.True(audit.Count >= 2, string.Join("\n", h.Log.Lines));
        Assert.Contains(audit, l => l.Contains("a.tmp", StringComparison.Ordinal));
        Assert.DoesNotContain(audit, l => l.Contains("important.docx", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repeated_cleaning_is_safe_and_the_second_run_recovers_nothing()
    {
        using var h = new Harness();
        SeedJunk(h);
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);

        var first = await RunOnce(h, settings);
        var second = await RunOnce(h, settings);
        var third = await RunOnce(h, settings);

        Assert.Equal(3000, first.BytesRecovered);
        Assert.Equal(0, second.BytesRecovered);
        Assert.Equal(0, second.ItemsCleaned);
        Assert.Equal(0, third.Errors);
        Assert.True(File.Exists(h.Temp.Combine("Users/tester/AppData/Local/Temp/fresh.tmp")));
    }

    private static async Task<CleanReport> RunOnce(Harness h, CleanSlateSettings settings)
    {
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        return await h.Engine.CleanAsync(PlanBuilder.Build(scan, PlanMode.Automatic), settings, null, CancellationToken.None);
    }

    [Fact]
    public async Task A_canceled_scan_reports_cancellation_and_changes_nothing()
    {
        using var h = new Harness();
        SeedJunk(h);
        var before = Harness.SnapshotTree(h.Temp.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var scan = await h.Engine.ScanAsync(Harness.Minimal(s => s.CleanUserTemp = true), null, cts.Token);

        Assert.True(scan.WasCanceled);
        Assert.Equal(before, Harness.SnapshotTree(h.Temp.Path));
    }

    [Fact]
    public async Task A_canceled_cleanup_stops_before_deleting_and_is_flagged()
    {
        using var h = new Harness();
        SeedJunk(h);
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        var plan = PlanBuilder.Build(scan, PlanMode.Automatic);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var clean = await h.Engine.CleanAsync(plan, settings, null, cts.Token);

        Assert.True(clean.WasCanceled);
        Assert.Equal(0, clean.ItemsCleaned);
        Assert.True(File.Exists(h.Temp.Combine("Users/tester/AppData/Local/Temp/a.tmp")));
    }

    [Fact]
    public async Task Concurrent_runs_are_rejected_while_another_operation_holds_the_gate()
    {
        using var h = new Harness();
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);

        using (h.Gate.Enter())
        {
            await Assert.ThrowsAsync<EngineBusyException>(() => h.Engine.ScanAsync(settings, null, CancellationToken.None));
            await Assert.ThrowsAsync<EngineBusyException>(() => h.Repair.RepairAsync(Array.Empty<RegistryIssue>(), false, null, CancellationToken.None));
        }

        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        Assert.False(scan.WasCanceled);
    }

    [Fact]
    public async Task Automatic_mode_never_runs_risky_or_confirmation_required_categories()
    {
        using var h = new Harness();
        h.Bin.Bytes = 9_000_000;
        h.Bin.Items = 12;
        h.Runner.ExitCode = 0;
        SeedJunk(h);
        var settings = Harness.Minimal(s =>
        {
            s.CleanUserTemp = true;
            s.EmptyRecycleBin = true;
            s.ClearActivityHistory = true;
        });
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);

        var plan = PlanBuilder.Build(scan, PlanMode.Automatic);
        var clean = await h.Engine.CleanAsync(plan, settings, null, CancellationToken.None);

        Assert.True(PlanBuilder.RequiresReview(scan));
        Assert.All(plan.Findings, f => Assert.True(f.AutoCleanAllowed && !f.ConfirmationRequired));
        Assert.Equal(0, h.Bin.EmptyCalls);
        Assert.Equal(3000, clean.BytesRecovered);
    }

    [Fact]
    public async Task Reviewed_mode_runs_a_confirmed_recycle_bin_operation_only_when_selected()
    {
        using var h = new Harness();
        h.Bin.Bytes = 9_000_000;
        h.Bin.Items = 12;
        var settings = Harness.Minimal(s => s.EmptyRecycleBin = true);
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        foreach (var f in scan.AllFindings)
        {
            f.IsSelected = false;
        }

        var empty = await h.Engine.CleanAsync(PlanBuilder.Build(scan, PlanMode.Reviewed), settings, null, CancellationToken.None);
        Assert.Equal(0, h.Bin.EmptyCalls);
        Assert.Equal(0, empty.ItemsCleaned);

        foreach (var f in scan.AllFindings.Where(f => f.IsEligible))
        {
            f.IsSelected = true;
        }

        await h.Engine.CleanAsync(PlanBuilder.Build(scan, PlanMode.Reviewed), settings, null, CancellationToken.None);
        Assert.Equal(1, h.Bin.EmptyCalls);
    }

    [Fact]
    public async Task Registry_findings_are_never_executed_by_the_engine()
    {
        using var h = new Harness(TestRegistry.Locations);
        h.RegistryProbe.AddFixedDrive("C:");
        h.Registry.Seed(TestRegistry.RunKey, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var settings = Harness.Minimal(s => s.ScanRegistry = true);
        var scan = await h.Engine.ScanAsync(settings, null, CancellationToken.None);
        foreach (var f in scan.AllFindings)
        {
            f.IsSelected = true;
        }

        var plan = PlanBuilder.Build(scan, PlanMode.Reviewed);
        await h.Engine.CleanAsync(plan, settings, null, CancellationToken.None);

        Assert.Empty(plan.Findings);
        Assert.NotNull(h.Registry.GetValue(TestRegistry.RunKey, "Gone"));
    }

    [Fact]
    public void Settings_persist_across_instances_and_unsupported_options_stay_off()
    {
        using var temp = new TempDirectory();
        var layout = new AppDataLayout(temp.Combine("data"));
        layout.EnsureCreated();
        var store = new SettingsStore(layout, NullAppLogger.Instance);
        var settings = store.Load();
        settings.EmptyRecycleBin = true;
        settings.ReviewBeforeCleaning = true;
        settings.ExcludedFolders.Add(@"C:\Keep");
        settings.ClearBrowserHistory = true;
        store.Save(settings);

        var reloaded = new SettingsStore(layout, NullAppLogger.Instance).Load();

        Assert.True(reloaded.EmptyRecycleBin);
        Assert.True(reloaded.ReviewBeforeCleaning);
        Assert.Contains(@"C:\Keep", reloaded.ExcludedFolders);
        Assert.False(reloaded.ClearBrowserHistory);
        Assert.False(reloaded.ClearBrowserCookies);
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        using var temp = new TempDirectory();
        var layout = new AppDataLayout(temp.Combine("data"));
        layout.EnsureCreated();
        File.WriteAllText(layout.SettingsFile, "{ not json");

        var loaded = new SettingsStore(layout, NullAppLogger.Instance).Load();

        Assert.True(loaded.CleanUserTemp);
        Assert.False(loaded.EmptyRecycleBin);
    }
}
