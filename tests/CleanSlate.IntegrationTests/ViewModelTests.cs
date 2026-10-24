using CleanSlate.Core.Models;
using CleanSlate.Presentation.Infrastructure;
using CleanSlate.Presentation.ViewModels;
using CleanSlate.TestSupport;
using Xunit;

namespace CleanSlate.IntegrationTests;

public sealed class MainViewModelTests
{
    private static string TempFile(PresentationHarness h, string name) => h.Core.Temp.Combine("Users/tester/AppData/Local/Temp/" + name);

    [Fact]
    public void Initial_state_is_ready_with_five_rows_and_a_clean_now_button()
    {
        using var h = new PresentationHarness();

        Assert.Equal(AppState.Ready, h.Main.State);
        Assert.Equal("Clean Now", h.Main.PrimaryButtonText);
        Assert.Equal(new[] { "SYSTEM JUNK", "CHROME", "EDGE", "REGISTRY", "PRIVACY" }, h.Main.Rows.Select(r => r.Title));
        Assert.True(h.Main.PrimaryCommand.CanExecute(null));
        Assert.False(h.Main.CancelCommand.CanExecute(null));
        Assert.Equal("Never cleaned yet", h.Main.LastCleanedText);
        Assert.True(h.Main.ShowAdminHint);
    }

    [Fact]
    public async Task One_click_flow_scans_cleans_reports_real_numbers_and_records_history()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 1000);
        h.UserTempFile("sub/b.tmp", 2000);
        h.UserTempFile("fresh.tmp", 10, DateTime.UtcNow);

        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.Equal(AppState.Completed, h.Main.State);
        Assert.Equal("Clean Again", h.Main.PrimaryButtonText);
        Assert.Contains("2.93 KB", h.Main.Headline);
        Assert.Contains("2 items removed", h.Main.Detail);
        Assert.False(File.Exists(TempFile(h, "a.tmp")));
        Assert.True(File.Exists(TempFile(h, "fresh.tmp")));
        var system = h.Main.Rows.Single(r => r.Group == CleanerGroup.SystemJunk);
        Assert.StartsWith("Cleaned", system.Status);
        Assert.Equal(RowKind.Success, system.Kind);
        var saved = h.History.GetLastCleanup();
        Assert.NotNull(saved);
        Assert.Equal(3000, saved!.TotalRecoveredBytes);
        Assert.Single(h.Notifications.Sent);
        Assert.True(h.Main.ViewDetailsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Disabled_categories_are_labelled_and_never_touched()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 100);

        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.Equal("Disabled in settings", h.Main.Rows.Single(r => r.Group == CleanerGroup.Chrome).Status);
        Assert.Equal("Disabled in settings", h.Main.Rows.Single(r => r.Group == CleanerGroup.Privacy).Status);
    }

    [Fact]
    public async Task Clean_again_after_completion_runs_a_fresh_scan()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 1000);
        await h.Main.PrimaryCommand.ExecuteAsync();
        h.UserTempFile("later.tmp", 500);

        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.Contains("500 B", h.Main.Headline);
        Assert.False(File.Exists(TempFile(h, "later.tmp")));
    }

    [Fact]
    public async Task Nothing_to_clean_is_reported_honestly()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);

        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.Equal(AppState.Completed, h.Main.State);
        Assert.Equal("Nothing to clean", h.Main.Headline);
    }

    [Fact]
    public async Task Done_returns_to_ready_and_keeps_the_row_results()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 1000);
        await h.Main.PrimaryCommand.ExecuteAsync();

        h.Main.DoneCommand.Execute(null);

        Assert.Equal(AppState.Ready, h.Main.State);
        Assert.Equal("Clean Now", h.Main.PrimaryButtonText);
        Assert.StartsWith("Cleaned", h.Main.Rows[0].Status);
        Assert.StartsWith("Last cleaned", h.Main.LastCleanedText);
    }

    [Fact]
    public async Task A_permanent_operation_forces_the_review_screen_even_in_one_click_mode()
    {
        using var h = new PresentationHarness(s => s.EmptyRecycleBin = true);
        h.Core.Bin.Bytes = 5_000_000;
        h.Core.Bin.Items = 3;

        var run = h.Main.PrimaryCommand.ExecuteAsync();
        var review = await PresentationHarness.WaitFor(() => h.Main.CurrentPage as ReviewViewModel);

        Assert.Equal(AppState.AwaitingReview, h.Main.State);
        Assert.True(review.ForcedByPolicy);
        Assert.True(review.HasPermanentSelection);
        Assert.Equal(0, h.Core.Bin.EmptyCalls);

        review.ConfirmCommand.Execute(null);
        await run;

        Assert.Equal(1, h.Core.Bin.EmptyCalls);
        Assert.Equal(AppState.Completed, h.Main.State);
        Assert.Null(h.Main.CurrentPage);
    }

    [Fact]
    public async Task Canceling_the_review_changes_nothing()
    {
        using var h = new PresentationHarness(s => s.EmptyRecycleBin = true);
        h.Core.Bin.Bytes = 5_000_000;
        h.Core.Bin.Items = 3;

        var run = h.Main.PrimaryCommand.ExecuteAsync();
        var review = await PresentationHarness.WaitFor(() => h.Main.CurrentPage as ReviewViewModel);
        review.CancelCommand.Execute(null);
        await run;

        Assert.Equal(0, h.Core.Bin.EmptyCalls);
        Assert.Equal(AppState.Ready, h.Main.State);
        Assert.Contains("Nothing was changed", h.Main.Detail);
    }

    [Fact]
    public async Task Review_mode_lets_the_user_deselect_a_category_before_anything_runs()
    {
        using var h = new PresentationHarness(s =>
        {
            s.CleanUserTemp = true;
            s.ReviewBeforeCleaning = true;
        });
        h.UserTempFile("a.tmp", 1000);

        var run = h.Main.PrimaryCommand.ExecuteAsync();
        var review = await PresentationHarness.WaitFor(() => h.Main.CurrentPage as ReviewViewModel);
        Assert.False(review.ForcedByPolicy);
        var item = review.AllItems.Single(i => i.CanSelect);
        Assert.True(item.IsSelected);
        item.IsSelected = false;
        Assert.False(review.ConfirmCommand.CanExecute(null));
        Assert.Equal("Nothing selected", review.ConfirmText);
        review.SelectAll(true);
        item.IsSelected = false;
        review.CancelCommand.Execute(null);
        await run;

        Assert.True(File.Exists(TempFile(h, "a.tmp")));
    }

    [Fact]
    public async Task Review_mode_cleans_only_what_stays_selected()
    {
        using var h = new PresentationHarness(s =>
        {
            s.CleanUserTemp = true;
            s.CleanCrashDumps = true;
            s.ReviewBeforeCleaning = true;
        });
        h.UserTempFile("a.tmp", 1000);
        h.Core.Temp.CreateFile("Users/tester/AppData/Local/CrashDumps/app.dmp", 3000, Harness.OldUtc);

        var run = h.Main.PrimaryCommand.ExecuteAsync();
        var review = await PresentationHarness.WaitFor(() => h.Main.CurrentPage as ReviewViewModel);
        review.AllItems.Single(i => i.Category.Contains("dump", StringComparison.OrdinalIgnoreCase)).IsSelected = false;
        review.ConfirmCommand.Execute(null);
        await run;

        Assert.False(File.Exists(TempFile(h, "a.tmp")));
        Assert.True(File.Exists(h.Core.Temp.Combine("Users/tester/AppData/Local/CrashDumps/app.dmp")));
    }

    [Fact]
    public async Task Cancel_during_a_scan_returns_to_ready_without_changing_anything()
    {
        var engine = new ControllableEngine
        {
            OnScan = async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("unreachable");
            }
        };
        using var h = new PresentationHarness(engineOverride: engine);

        var run = h.Main.PrimaryCommand.ExecuteAsync();
        await engine.Started.Task;
        Assert.Equal(AppState.Scanning, h.Main.State);
        Assert.Equal("Scanning...", h.Main.PrimaryButtonText);
        Assert.False(h.Main.PrimaryCommand.CanExecute(null));
        Assert.True(h.Main.CancelCommand.CanExecute(null));
        h.Main.CancelCommand.Execute(null);
        await run;

        Assert.Equal(AppState.Ready, h.Main.State);
        Assert.Equal("Canceled", h.Main.Headline);
        Assert.False(h.Main.HasError);
        Assert.True(h.Main.PrimaryCommand.CanExecute(null));
    }

    [Fact]
    public async Task Unexpected_engine_failures_surface_as_an_error_and_the_app_stays_usable()
    {
        var engine = new ControllableEngine { OnScan = (_, _) => throw new InvalidOperationException("boom") };
        using var h = new PresentationHarness(engineOverride: engine);

        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.Equal(AppState.Ready, h.Main.State);
        Assert.True(h.Main.HasError);
        Assert.Contains("boom", h.Main.ErrorMessage);
        Assert.True(h.Main.PrimaryCommand.CanExecute(null));
        Assert.Contains(h.Core.Log.Lines, l => l.StartsWith("ERROR", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_run_rejection_is_shown_to_the_user()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);

        using (h.Core.Gate.Enter())
        {
            await h.Main.PrimaryCommand.ExecuteAsync();
        }

        Assert.True(h.Main.HasError);
        Assert.Contains("already running", h.Main.ErrorMessage);
        Assert.Equal(AppState.Ready, h.Main.State);
    }

    [Fact]
    public async Task Navigation_is_blocked_while_a_run_is_in_progress()
    {
        var gate = new TaskCompletionSource();
        var engine = new ControllableEngine { OnScan = async (_, _) => { await gate.Task; return new ScanReport(); } };
        using var h = new PresentationHarness(engineOverride: engine);

        var run = h.Main.PrimaryCommand.ExecuteAsync();
        await engine.Started.Task;
        Assert.False(h.Main.OpenSettingsCommand.CanExecute(null));
        Assert.False(h.Main.RestartAsAdministratorCommand.CanExecute(null));
        gate.SetResult();
        await run;

        Assert.True(h.Main.OpenSettingsCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restart_as_administrator_requires_confirmation_and_shuts_down_only_when_started()
    {
        using var h = new PresentationHarness();
        h.Dialogs.Answer = false;
        await h.Main.RestartAsAdministratorCommand.ExecuteAsync();
        Assert.Equal(0, h.Elevation.RestartCalls);

        h.Dialogs.Answer = true;
        h.Elevation.Result = ElevationRestartResult.Declined;
        await h.Main.RestartAsAdministratorCommand.ExecuteAsync();
        Assert.Equal(1, h.Elevation.RestartCalls);
        Assert.Equal(0, h.Lifecycle.ShutdownCalls);
        Assert.Single(h.Dialogs.Messages);

        h.Elevation.Result = ElevationRestartResult.Started;
        await h.Main.RestartAsAdministratorCommand.ExecuteAsync();
        Assert.Equal(1, h.Lifecycle.ShutdownCalls);
    }

    [Fact]
    public async Task Details_page_shows_real_per_category_results()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 1000);
        await h.Main.PrimaryCommand.ExecuteAsync();

        h.Main.ViewDetailsCommand.Execute(null);

        var details = Assert.IsType<DetailsViewModel>(h.Main.CurrentPage);
        Assert.Contains(details.Rows, r => r.Cleaned == "1" && r.Recovered.StartsWith("1000 B", StringComparison.Ordinal) || r.Recovered.Contains("1000", StringComparison.Ordinal));
        details.BackCommand.Execute(null);
        Assert.Null(h.Main.CurrentPage);
    }
}

public sealed class RegistryReviewTests
{
    private static void SeedBroken(PresentationHarness h)
    {
        h.Core.RegistryProbe.AddFixedDrive("C:");
        h.Core.Registry.Seed(TestRegistry.RunKey, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        h.Core.Registry.Seed(TestRegistry.RunKey, "Other", RegistryData.String(@"C:\Gone\other.exe"));
    }

    private static PresentationHarness Create() => new(s => s.ScanRegistry = true, registryLocations: TestRegistry.Locations);

    [Fact]
    public async Task One_click_flow_never_changes_the_registry_but_surfaces_the_issues()
    {
        using var h = Create();
        SeedBroken(h);
        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.Equal(AppState.Completed, h.Main.State);
        Assert.Equal(2, h.Main.RegistryIssueCount);
        Assert.Contains("2 issues found", h.Main.Rows.Single(r => r.Group == CleanerGroup.Registry).Status);
        Assert.True(h.Main.ReviewRegistryCommand.CanExecute(null));
        Assert.NotNull(h.Core.Registry.GetValue(TestRegistry.RunKey, "Gone"));
    }

    [Fact]
    public async Task Repair_requires_selection_and_confirmation_then_backs_up_and_can_be_restored()
    {
        using var h = Create();
        SeedBroken(h);
        await h.Main.PrimaryCommand.ExecuteAsync();
        h.Main.ReviewRegistryCommand.Execute(null);
        var page = Assert.IsType<RegistryReviewViewModel>(h.Main.CurrentPage);

        Assert.All(page.Issues, i => Assert.False(i.IsSelected));
        Assert.False(page.RepairCommand.CanExecute(null));

        page.Issues.Single(i => i.Issue.ValueName == "Gone").IsSelected = true;
        h.Dialogs.Answer = false;
        await page.RepairCommand.ExecuteAsync();
        Assert.NotNull(h.Core.Registry.GetValue(TestRegistry.RunKey, "Gone"));

        h.Dialogs.Answer = true;
        await page.RepairCommand.ExecuteAsync();

        Assert.Null(h.Core.Registry.GetValue(TestRegistry.RunKey, "Gone"));
        Assert.NotNull(h.Core.Registry.GetValue(TestRegistry.RunKey, "Other"));
        Assert.Equal(1, h.Core.RestorePoint.Calls);
        Assert.Single(page.Backups);
        Assert.Contains("Repaired", page.Issues.Single(i => i.Issue.ValueName == "Gone").Status);
        Assert.Equal(1, h.Main.RegistryIssueCount);

        await page.RestoreAsync(page.Backups[0].Info);
        Assert.NotNull(h.Core.Registry.GetValue(TestRegistry.RunKey, "Gone"));
        Assert.Contains("restore", page.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Machine_wide_entries_cannot_be_selected_without_administrator_rights()
    {
        using var h = new PresentationHarness(s => s.ScanRegistry = true, registryLocations: CleanSlate.Cleaners.Registry.RegistryScanLocations.Production);
        h.Core.RegistryProbe.AddFixedDrive("C:");
        var lm = new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        h.Core.Registry.Seed(lm, "MachineGone", RegistryData.String(@"C:\Gone\machine.exe"));
        await h.Main.PrimaryCommand.ExecuteAsync();
        h.Main.ReviewRegistryCommand.Execute(null);
        var page = Assert.IsType<RegistryReviewViewModel>(h.Main.CurrentPage);

        var item = Assert.Single(page.Issues);
        item.IsSelected = true;

        Assert.False(item.CanSelect);
        Assert.False(item.IsSelected);
        Assert.Equal("Requires administrator", item.Status);
    }
}

public sealed class SettingsViewModelTests
{
    private static SettingsViewModel Open(PresentationHarness h)
    {
        h.Main.OpenSettingsCommand.Execute(null);
        return Assert.IsType<SettingsViewModel>(h.Main.CurrentPage);
    }

    [Fact]
    public async Task Risky_options_need_consent_and_stay_off_when_declined()
    {
        using var h = new PresentationHarness();
        var settings = Open(h);
        var toggle = settings.Toggle("recycle");

        h.Dialogs.Answer = false;
        await toggle.SetAsync(true);
        Assert.False(h.Settings.Current.EmptyRecycleBin);
        Assert.False(toggle.IsOn);
        Assert.Single(h.Dialogs.Confirmations);

        h.Dialogs.Answer = true;
        await toggle.SetAsync(true);
        Assert.True(h.Settings.Current.EmptyRecycleBin);
        Assert.True(h.Store.Stored.EmptyRecycleBin);
        Assert.True(toggle.IsOn);
    }

    [Fact]
    public async Task Turning_a_risky_option_off_never_prompts()
    {
        using var h = new PresentationHarness(s => s.EmptyRecycleBin = true);
        var settings = Open(h);

        await settings.Toggle("recycle").SetAsync(false);

        Assert.Empty(h.Dialogs.Confirmations);
        Assert.False(h.Store.Stored.EmptyRecycleBin);
    }

    [Fact]
    public async Task Launch_on_startup_registers_and_unregisters_the_per_user_entry()
    {
        using var h = new PresentationHarness();
        var settings = Open(h);

        await settings.Toggle("startup").SetAsync(true);
        Assert.True(h.Startup.IsRegistered);
        Assert.Equal(@"C:\Apps\CleanSlate\CleanSlate.exe", h.Startup.Path);

        await settings.Toggle("startup").SetAsync(false);
        Assert.False(h.Startup.IsRegistered);
    }

    [Fact]
    public async Task A_failed_startup_registration_leaves_the_setting_off_and_explains()
    {
        using var h = new PresentationHarness();
        h.Startup.Fail = true;
        var settings = Open(h);

        await settings.Toggle("startup").SetAsync(true);

        Assert.False(h.Settings.Current.LaunchOnStartup);
        Assert.False(settings.Toggle("startup").IsOn);
        Assert.Contains("refused", settings.Message);
    }

    [Fact]
    public async Task Settings_cannot_change_while_a_run_is_active()
    {
        var gate = new TaskCompletionSource();
        var engine = new ControllableEngine { OnScan = async (_, _) => { await gate.Task; return new ScanReport(); } };
        using var h = new PresentationHarness(engineOverride: engine);
        var settings = Open(h);
        var run = h.Main.PrimaryCommand.ExecuteAsync();
        await engine.Started.Task;

        var toggle = settings.Toggle("user-temp");
        var before = toggle.IsOn;
        await toggle.SetAsync(!before);

        Assert.Equal(before, toggle.IsOn);
        gate.SetResult();
        await run;
    }

    [Fact]
    public void Exclusions_are_added_removed_and_persisted()
    {
        using var h = new PresentationHarness();
        var settings = Open(h);

        settings.AddExclusion(@"C:\Keep\Me", folder: true);
        settings.AddExclusion(@"C:\Keep\Me", folder: true);
        settings.AddExclusion(@"C:\x\file.tmp", folder: false);

        Assert.Single(settings.ExcludedFolders);
        Assert.Equal(new[] { @"C:\Keep\Me" }, h.Store.Stored.ExcludedFolders);
        Assert.Equal(new[] { @"C:\x\file.tmp" }, h.Store.Stored.ExcludedFiles);

        settings.RemoveExclusion(@"C:\Keep\Me");
        Assert.Empty(h.Store.Stored.ExcludedFolders);
    }

    [Fact]
    public async Task Excluded_folders_are_respected_by_the_next_run()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("keep/a.tmp", 100);
        h.UserTempFile("b.tmp", 100);
        var settings = Open(h);
        settings.AddExclusion(h.Core.Temp.Combine("Users/tester/AppData/Local/Temp/keep"), folder: true);
        settings.BackCommand.Execute(null);

        await h.Main.PrimaryCommand.ExecuteAsync();

        Assert.True(File.Exists(h.Core.Temp.Combine("Users/tester/AppData/Local/Temp/keep/a.tmp")));
        Assert.False(File.Exists(h.Core.Temp.Combine("Users/tester/AppData/Local/Temp/b.tmp")));
    }

    [Fact]
    public async Task Reset_restores_defaults_after_confirmation()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = false);
        var settings = Open(h);

        h.Dialogs.Answer = false;
        await settings.ResetCommand.ExecuteAsync();
        Assert.False(h.Settings.Current.CleanUserTemp);

        h.Dialogs.Answer = true;
        await settings.ResetCommand.ExecuteAsync();
        Assert.True(h.Settings.Current.CleanUserTemp);
        Assert.True(settings.Toggle("user-temp").IsOn);
    }

    [Fact]
    public async Task Clearing_history_requires_confirmation_and_clears_logs()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 100);
        await h.Main.PrimaryCommand.ExecuteAsync();
        var settings = Open(h);
        settings.RefreshHistory();
        Assert.NotEmpty(settings.History);

        h.Dialogs.Answer = false;
        await settings.ClearHistoryCommand.ExecuteAsync();
        Assert.NotEmpty(settings.History);

        h.Dialogs.Answer = true;
        await settings.ClearHistoryCommand.ExecuteAsync();
        Assert.Empty(settings.History);
        Assert.Equal(1, h.Logs.ClearCalls);
    }

    [Fact]
    public async Task Report_export_writes_the_history_to_the_chosen_file()
    {
        using var h = new PresentationHarness(s => s.CleanUserTemp = true);
        h.UserTempFile("a.tmp", 100);
        await h.Main.PrimaryCommand.ExecuteAsync();
        var settings = Open(h);
        h.Dialogs.SaveFile = h.Core.Temp.Combine("report.txt");

        settings.ExportReportCommand.Execute(null);

        Assert.Contains("CleanSlate report", File.ReadAllText(h.Dialogs.SaveFile));
    }

    [Fact]
    public void Unsupported_features_are_listed_with_reasons()
    {
        using var h = new PresentationHarness();
        var settings = Open(h);

        Assert.Contains(settings.UnsupportedFeatures, u => u.Name.Contains("history", StringComparison.OrdinalIgnoreCase) && u.Reason.Length > 20);
        Assert.Contains(settings.UnsupportedFeatures, u => u.Name.Contains("Update", StringComparison.OrdinalIgnoreCase));
    }
}
