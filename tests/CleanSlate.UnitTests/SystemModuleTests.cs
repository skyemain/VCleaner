using CleanSlate.Cleaners.Registry;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.TestSupport;
using Xunit;

namespace CleanSlate.UnitTests;

public sealed class SystemModuleTests
{
    private static ScanContext Scan(CleanSlateSettings settings, bool elevated = false, ClaimSet? claims = null) => new()
    {
        Settings = settings,
        Reporter = NullModuleReporter.Instance,
        Claims = claims ?? new ClaimSet(),
        Exclusions = ExclusionPolicy.None,
        IsElevated = elevated
    };

    private static CleanContext Clean(CleanSlateSettings settings, bool elevated = false) => new()
    {
        Settings = settings,
        Reporter = NullModuleReporter.Instance,
        Exclusions = ExclusionPolicy.None,
        IsElevated = elevated
    };

    [Fact]
    public async Task User_temp_finds_only_old_files_and_cleans_them_with_real_numbers()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/old1.tmp", 1000, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/nested/old2.log", 500, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/fresh.tmp", 77, DateTime.UtcNow);
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);
        var module = h.Module("system.user-temp");

        var scan = await module.ScanAsync(Scan(settings), CancellationToken.None);
        Assert.Equal(2, scan.EligibleCount);
        Assert.Equal(1500, scan.EligibleBytes);
        Assert.Equal(3, scan.ItemsScanned);

        var clean = await module.CleanAsync(Clean(settings), scan.Findings, CancellationToken.None);
        Assert.Equal(2, clean.ItemsCleaned);
        Assert.Equal(1500, clean.BytesRecovered);
        Assert.Equal(1, clean.DirectoriesRemoved);
        Assert.True(File.Exists(h.Temp.Combine("Users/tester/AppData/Local/Temp/fresh.tmp")));
        Assert.False(Directory.Exists(h.Temp.Combine("Users/tester/AppData/Local/Temp/nested")));
    }

    [Fact]
    public async Task A_temp_variable_pointing_at_a_personal_folder_is_refused()
    {
        using var h = new Harness();
        var docs = h.Temp.CreateFile("Users/tester/Documents/report.docx", 10, Harness.OldUtc);
        var evilPaths = h.Paths with { UserTemp = h.Paths.Documents, UserTmp = h.Paths.UserProfile };
        var modules = CleanSlate.Cleaners.CleanerCatalog.CreateModules(evilPaths, h.Deletion, h.Registry, h.Scanner, h.FileProbe, h.Runner, h.Bin, h.Browser, h.Log);
        var module = modules.Single(m => m.ModuleId == "system.user-temp");

        var scan = await module.ScanAsync(Scan(Harness.Minimal(s => s.CleanUserTemp = true)), CancellationToken.None);

        Assert.Equal(0, scan.EligibleCount);
        Assert.All(scan.Findings, f => Assert.False(f.IsEligible));
        Assert.True(File.Exists(docs));
    }

    [Fact]
    public async Task Duplicate_temp_and_tmp_roots_are_counted_once()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/a.tmp", 100, Harness.OldUtc);
        var paths = h.Paths with { UserTmp = h.Paths.UserTemp + Path.DirectorySeparatorChar };
        var modules = CleanSlate.Cleaners.CleanerCatalog.CreateModules(paths, h.Deletion, h.Registry, h.Scanner, h.FileProbe, h.Runner, h.Bin, h.Browser, h.Log);

        var scan = await modules.Single(m => m.ModuleId == "system.user-temp")
            .ScanAsync(Scan(Harness.Minimal(s => s.CleanUserTemp = true)), CancellationToken.None);

        Assert.Equal(1, scan.EligibleCount);
    }

    [Fact]
    public async Task Overlapping_claims_prevent_double_counting_across_modules()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/shared.tmp", 100, Harness.OldUtc);
        var claims = new ClaimSet();
        var settings = Harness.Minimal(s => s.CleanUserTemp = true);

        var first = await h.Module("system.user-temp").ScanAsync(Scan(settings, claims: claims), CancellationToken.None);
        var second = await h.Module("system.user-temp").ScanAsync(Scan(settings, claims: claims), CancellationToken.None);

        Assert.Equal(1, first.EligibleCount);
        Assert.Equal(0, second.EligibleCount);
    }

    [Fact]
    public async Task Thumbnails_and_shader_cache_use_explicit_locations_and_patterns()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Users/tester/AppData/Local/Microsoft/Windows/Explorer/thumbcache_256.db", 100);
        h.Temp.CreateFile("Users/tester/AppData/Local/Microsoft/Windows/Explorer/iconcache_32.db", 100);
        h.Temp.CreateFile("Users/tester/AppData/Local/Microsoft/Windows/Explorer/NavPane/other.dat", 100);
        h.Temp.CreateFile("Users/tester/AppData/Local/D3DSCache/abc/shader.bin", 300);
        var settings = Harness.Minimal(s =>
        {
            s.CleanThumbnailCache = true;
            s.CleanShaderCache = true;
        });

        var thumbs = await h.Module("system.thumbnails").ScanAsync(Scan(settings), CancellationToken.None);
        var shaders = await h.Module("system.shader-cache").ScanAsync(Scan(settings), CancellationToken.None);

        var thumb = Assert.Single(thumbs.Findings);
        Assert.EndsWith("thumbcache_256.db", thumb.Location);
        Assert.Equal(300, Assert.Single(shaders.Findings).EstimatedBytes);
    }

    [Fact]
    public async Task Error_reports_and_crash_dumps_respect_age_and_extension_rules()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Users/tester/AppData/Local/Microsoft/Windows/WER/ReportArchive/Crash_1/Report.wer", 200, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Microsoft/Windows/WER/ReportQueue/Fresh/Report.wer", 200, DateTime.UtcNow);
        h.Temp.CreateFile("Users/tester/AppData/Local/CrashDumps/app.exe.1234.dmp", 5000, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/CrashDumps/notes.txt", 5, Harness.OldUtc);
        var settings = Harness.Minimal(s =>
        {
            s.CleanErrorReports = true;
            s.CleanCrashDumps = true;
        });

        var wer = await h.Module("system.wer-user").ScanAsync(Scan(settings), CancellationToken.None);
        var dumps = await h.Module("system.crash-dumps-user").ScanAsync(Scan(settings), CancellationToken.None);

        Assert.Single(wer.Findings);
        Assert.Equal(5000, Assert.Single(dumps.Findings).EstimatedBytes);
    }

    [Fact]
    public async Task Elevated_modules_are_skipped_when_not_elevated_and_scan_system_locations_when_elevated()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Windows/Minidump/051524-1.dmp", 2000, Harness.OldUtc);
        h.Temp.CreateFile("Windows/MEMORY.DMP", 9000, Harness.OldUtc);
        h.Temp.CreateFile("Windows/Logs/CBS/CbsPersist_20240101.log", 400, Harness.OldUtc);
        h.Temp.CreateFile("Windows/Logs/CBS/CBS.log", 400, Harness.OldUtc);
        h.Temp.CreateFile("Windows/System32/winevt/Logs/Security.evtx", 400, Harness.OldUtc);
        var settings = Harness.Minimal(s =>
        {
            s.CleanCrashDumps = true;
            s.CleanWindowsLogs = true;
        });

        var dumps = h.Module("system.crash-dumps-system");
        var logs = h.Module("system.windows-logs");
        Assert.True(dumps.RequiresElevation);
        Assert.True(logs.RequiresElevation);

        var dumpScan = await dumps.ScanAsync(Scan(settings, elevated: true), CancellationToken.None);
        var logScan = await logs.ScanAsync(Scan(settings, elevated: true), CancellationToken.None);

        Assert.Equal(new[] { 2000L, 9000L }, dumpScan.Findings.Select(f => f.EstimatedBytes).Order().ToArray());
        var logFinding = Assert.Single(logScan.Findings);
        Assert.EndsWith("CbsPersist_20240101.log", logFinding.Location);
    }

    [Fact]
    public async Task Windows_temp_files_are_cleaned_and_foreign_files_are_untouched()
    {
        using var h = new Harness();
        var temp = h.Temp.CreateFile("Windows/Temp/leftover.tmp", 123, Harness.OldUtc);
        var system = h.Temp.CreateFile("Windows/System32/kernel.dll", 123, Harness.OldUtc);
        var settings = Harness.Minimal(s => s.CleanWindowsTemp = true);
        var module = h.Module("system.windows-temp");

        var scan = await module.ScanAsync(Scan(settings), CancellationToken.None);
        var clean = await module.CleanAsync(Clean(settings), scan.Findings, CancellationToken.None);

        Assert.Equal(1, clean.ItemsCleaned);
        Assert.False(File.Exists(temp));
        Assert.True(File.Exists(system));
    }

    [Fact]
    public async Task Recycle_bin_is_opt_in_reports_size_and_is_permanent_and_confirmed()
    {
        using var h = new Harness();
        h.Bin.Bytes = 5000;
        h.Bin.Items = 7;
        var module = h.Module("system.recycle-bin");

        Assert.False(module.IsEnabled(new CleanSlateSettings()));
        var settings = new CleanSlateSettings { EmptyRecycleBin = true };
        Assert.True(module.IsEnabled(settings));

        var scan = await module.ScanAsync(Scan(settings), CancellationToken.None);
        var finding = Assert.Single(scan.Findings);
        Assert.Equal(5000, finding.EstimatedBytes);
        Assert.True(finding.ConfirmationRequired);
        Assert.False(finding.AutoCleanAllowed);
        Assert.Equal(RiskLevel.High, finding.Risk);
        Assert.True(finding.IsPermanent);

        var clean = await module.CleanAsync(Clean(settings), scan.Findings, CancellationToken.None);
        Assert.Equal(5000, clean.BytesRecovered);
        Assert.Equal(7, clean.ItemsCleaned);
        Assert.Equal(1, h.Bin.EmptyCalls);
    }

    [Fact]
    public async Task Recycle_bin_failures_are_reported_as_errors_not_recovered_space()
    {
        using var h = new Harness();
        h.Bin.Bytes = 5000;
        h.Bin.Items = 7;
        h.Bin.FailOnEmpty = true;
        var settings = new CleanSlateSettings { EmptyRecycleBin = true };
        var module = h.Module("system.recycle-bin");
        var scan = await module.ScanAsync(Scan(settings), CancellationToken.None);

        var clean = await module.CleanAsync(Clean(settings), scan.Findings, CancellationToken.None);

        Assert.Equal(0, clean.BytesRecovered);
        Assert.Equal(1, clean.Errors);
    }

    [Fact]
    public async Task Servicing_modules_are_unavailable_or_skipped_until_supported_and_elevated()
    {
        using var h = new Harness();
        var settings = new CleanSlateSettings { CleanWindowsUpdateComponents = true, CleanDeliveryOptimization = true };
        var wu = h.Module("system.windows-update-cleanup");
        var dop = h.Module("system.delivery-optimization");

        Assert.Equal(ModuleScanStatus.Skipped, (await wu.ScanAsync(Scan(settings, elevated: false), CancellationToken.None)).Status);
        Assert.Equal(ModuleScanStatus.Unavailable, (await wu.ScanAsync(Scan(settings, elevated: true), CancellationToken.None)).Status);
        Assert.Equal(ModuleScanStatus.Unavailable, (await dop.ScanAsync(Scan(settings, elevated: true), CancellationToken.None)).Status);

        h.Temp.CreateFile("Windows/System32/dism.exe", 1);
        var scan = await wu.ScanAsync(Scan(settings, elevated: true), CancellationToken.None);
        var finding = Assert.Single(scan.Findings);
        Assert.Equal(ActionType.RunServicing, finding.Action);
        Assert.True(finding.ConfirmationRequired);
        Assert.Equal(0, finding.EstimatedBytes);
    }

    [Fact]
    public async Task Windows_update_cleanup_runs_only_the_fixed_supported_command()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Windows/System32/dism.exe", 1);
        h.Privilege.IsElevated = true;
        var settings = new CleanSlateSettings { CleanWindowsUpdateComponents = true };
        var wu = h.Module("system.windows-update-cleanup");
        var scan = await wu.ScanAsync(Scan(settings, elevated: true), CancellationToken.None);

        var clean = await wu.CleanAsync(Clean(settings, elevated: true), scan.Findings, CancellationToken.None);

        var call = Assert.Single(h.Runner.Calls);
        Assert.EndsWith("dism.exe", call.File);
        Assert.Equal("/Online /Cleanup-Image /StartComponentCleanup", call.Args);
        Assert.Equal(1, clean.ItemsCleaned);
        Assert.Equal(0, clean.BytesRecovered);

        h.Runner.ExitCode = 5;
        var failed = await wu.CleanAsync(Clean(settings, elevated: true), scan.Findings, CancellationToken.None);
        Assert.Equal(1, failed.Errors);
        Assert.Equal(0, failed.ItemsCleaned);
    }

    [Fact]
    public async Task Delivery_optimization_measures_the_cache_and_uses_the_supported_cmdlet()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Windows/System32/WindowsPowerShell/v1.0/powershell.exe", 1);
        h.Temp.CreateFile("Windows/ServiceProfiles/NetworkService/AppData/Local/Microsoft/Windows/DeliveryOptimization/Cache/x/file", 4096);
        var settings = new CleanSlateSettings { CleanDeliveryOptimization = true };
        var module = h.Module("system.delivery-optimization");

        var scan = await module.ScanAsync(Scan(settings, elevated: true), CancellationToken.None);
        Assert.Equal(4096, Assert.Single(scan.Findings).EstimatedBytes);

        await module.CleanAsync(Clean(settings, elevated: true), scan.Findings, CancellationToken.None);
        Assert.Contains("Delete-DeliveryOptimizationCache -Force", Assert.Single(h.Runner.Calls).Args);
    }
}

public sealed class PrivacyModuleTests
{
    private static readonly RegistryKeyRef Explorer = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer");

    [Fact]
    public async Task Privacy_cleanup_is_disabled_by_default_and_never_automatic()
    {
        using var h = new Harness();
        var module = h.Module("privacy.activity");
        Assert.False(module.IsEnabled(new CleanSlateSettings()));

        h.Registry.Seed(Explorer.Child("RunMRU"), "a", RegistryData.String("cmd\\1"));
        var settings = new CleanSlateSettings { ClearActivityHistory = true };
        var scan = await module.ScanAsync(new ScanContext
        {
            Settings = settings,
            Reporter = NullModuleReporter.Instance,
            Claims = new ClaimSet(),
            Exclusions = ExclusionPolicy.None
        }, CancellationToken.None);

        Assert.All(scan.Findings, f =>
        {
            Assert.True(f.ConfirmationRequired);
            Assert.False(f.AutoCleanAllowed);
        });
        Assert.Equal(0, h.Registry.WriteCount);
    }

    [Fact]
    public async Task Clears_values_only_keeps_keys_and_never_touches_source_files_or_unrelated_settings()
    {
        using var h = new Harness();
        var run = Explorer.Child("RunMRU");
        var typed = Explorer.Child("TypedPaths");
        var recent = Explorer.Child("RecentDocs");
        h.Registry.Seed(run, "a", RegistryData.String("notepad\\1"));
        h.Registry.Seed(run, "MRUList", RegistryData.String("a"));
        h.Registry.Seed(typed, "url1", RegistryData.String(@"C:\Work"));
        h.Registry.Seed(recent, "0", new RegistryData(RegistryDataKind.Binary, new byte[] { 1 }));
        h.Registry.Seed(recent.Child(".txt"), "0", new RegistryData(RegistryDataKind.Binary, new byte[] { 2 }));
        h.Registry.Seed(Explorer, "ShowFrequent", new RegistryData(RegistryDataKind.DWord, 1));
        h.Registry.Seed(Explorer.Child("Advanced"), "Hidden", new RegistryData(RegistryDataKind.DWord, 1));
        var source = h.Temp.CreateFile("Users/tester/Documents/important.txt", 10);
        h.Temp.CreateFile("Users/tester/AppData/Roaming/Microsoft/Windows/Recent/important.txt.lnk", 100, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Roaming/Microsoft/Windows/Recent/AutomaticDestinations/pinned.automaticDestinations-ms", 100, Harness.OldUtc);

        var settings = new CleanSlateSettings { ClearActivityHistory = true };
        var module = h.Module("privacy.activity");
        var scan = await module.ScanAsync(new ScanContext { Settings = settings, Reporter = NullModuleReporter.Instance, Claims = new ClaimSet(), Exclusions = ExclusionPolicy.None }, CancellationToken.None);
        var clean = await module.CleanAsync(new CleanContext { Settings = settings, Reporter = NullModuleReporter.Instance, Exclusions = ExclusionPolicy.None }, scan.Findings, CancellationToken.None);

        Assert.Empty(h.Registry.GetValueNames(run));
        Assert.Empty(h.Registry.GetValueNames(typed));
        Assert.Empty(h.Registry.GetValueNames(recent));
        Assert.Empty(h.Registry.GetValueNames(recent.Child(".txt")));
        Assert.True(h.Registry.KeyExists(run));
        Assert.True(h.Registry.KeyExists(recent.Child(".txt")));
        Assert.NotNull(h.Registry.GetValue(Explorer, "ShowFrequent"));
        Assert.NotNull(h.Registry.GetValue(Explorer.Child("Advanced"), "Hidden"));
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(h.Temp.Combine("Users/tester/AppData/Roaming/Microsoft/Windows/Recent/important.txt.lnk")));
        Assert.True(File.Exists(h.Temp.Combine("Users/tester/AppData/Roaming/Microsoft/Windows/Recent/AutomaticDestinations/pinned.automaticDestinations-ms")));
        Assert.Equal(0, clean.Errors);
        Assert.Equal(1 + 2 + 1 + 1 + 1, clean.ItemsCleaned);
    }

    [Fact]
    public async Task Only_the_categories_the_user_selected_are_cleared()
    {
        using var h = new Harness();
        var run = Explorer.Child("RunMRU");
        var typed = Explorer.Child("TypedPaths");
        h.Registry.Seed(run, "a", RegistryData.String("x"));
        h.Registry.Seed(typed, "url1", RegistryData.String("y"));
        var settings = new CleanSlateSettings { ClearActivityHistory = true, ClearRunHistory = true, ClearExplorerAddressHistory = false, ClearRecentItems = false };
        var module = h.Module("privacy.activity");

        var scan = await module.ScanAsync(new ScanContext { Settings = settings, Reporter = NullModuleReporter.Instance, Claims = new ClaimSet(), Exclusions = ExclusionPolicy.None }, CancellationToken.None);
        await module.CleanAsync(new CleanContext { Settings = settings, Reporter = NullModuleReporter.Instance, Exclusions = ExclusionPolicy.None }, scan.Findings, CancellationToken.None);

        Assert.Empty(h.Registry.GetValueNames(run));
        Assert.Single(h.Registry.GetValueNames(typed));
    }
}

public sealed class ConfigurationTests
{
    [Fact]
    public void Defaults_match_the_product_specification()
    {
        var s = new CleanSlateSettings();
        Assert.True(s.FollowSystemTheme);
        Assert.False(s.LaunchOnStartup);
        Assert.False(s.MinimizeToTray);
        Assert.True(s.ShowCompletionNotifications);
        Assert.True(s.CleanUserTemp && s.CleanWindowsTemp && s.CleanThumbnailCache && s.CleanShaderCache && s.CleanErrorReports);
        Assert.True(s.CleanChromeCache && s.CleanEdgeCache);
        Assert.False(s.EmptyRecycleBin);
        Assert.False(s.ClearBrowserHistory);
        Assert.False(s.ClearBrowserCookies);
        Assert.False(s.ClearActivityHistory);
        Assert.False(s.ReviewBeforeCleaning);
        Assert.True(s.ScanRegistry);
        Assert.True(s.CreateRestorePoint);
        Assert.False(s.CleanWindowsUpdateComponents);
        Assert.False(s.CleanDeliveryOptimization);
        Assert.False(s.ClearBrowserSiteStorage);
    }

    [Fact]
    public void Settings_round_trip_through_disk_and_survive_corruption()
    {
        using var temp = new TempDirectory();
        var layout = new AppDataLayout(temp.Combine("data"));
        var store = new SettingsStore(layout, new RecordingLogger());

        Assert.False(store.Load().ReviewBeforeCleaning);

        var settings = new CleanSlateSettings { ReviewBeforeCleaning = true, EmptyRecycleBin = true, MinimizeToTray = true };
        settings.ExcludedFolders.Add(@"C:\Keep");
        settings.ExcludedFiles.Add(@"C:\Keep\a.txt");
        settings.ExcludedFiles.Add("  ");
        store.Save(settings);

        var loaded = store.Load();
        Assert.True(loaded.ReviewBeforeCleaning);
        Assert.True(loaded.EmptyRecycleBin);
        Assert.True(loaded.MinimizeToTray);
        Assert.Equal(new[] { @"C:\Keep" }, loaded.ExcludedFolders);
        Assert.Equal(new[] { @"C:\Keep\a.txt" }, loaded.ExcludedFiles);

        File.WriteAllText(layout.SettingsFile, "{ this is not json");
        var recovered = store.Load();
        Assert.False(recovered.ReviewBeforeCleaning);
        Assert.True(File.Exists(layout.SettingsFile + ".corrupt"));

        store.Save(settings);
        store.Reset();
        Assert.False(store.Load().ReviewBeforeCleaning);
    }

    [Fact]
    public void Settings_file_never_enables_unsupported_browser_history_or_cookie_cleaning()
    {
        using var temp = new TempDirectory();
        var layout = new AppDataLayout(temp.Combine("data"));
        Directory.CreateDirectory(layout.Root);
        File.WriteAllText(layout.SettingsFile, "{\"ClearBrowserHistory\":true,\"ClearBrowserCookies\":true}");

        var loaded = new SettingsStore(layout, new RecordingLogger()).Load();

        Assert.False(loaded.ClearBrowserHistory);
        Assert.False(loaded.ClearBrowserCookies);
    }

    [Fact]
    public void History_is_stored_listed_capped_and_cleared()
    {
        using var temp = new TempDirectory();
        var layout = new AppDataLayout(temp.Combine("data"));
        var store = new HistoryStore(layout, new RecordingLogger());
        Assert.Null(store.GetLastCleanup());

        var scanOnly = new SessionRecord { ScanTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var cleaned = new SessionRecord
        {
            ScanTimestamp = DateTimeOffset.UtcNow.AddMinutes(-3),
            CleanupTimestamp = DateTimeOffset.UtcNow.AddMinutes(-2),
            TotalRecoveredBytes = 1234,
            Categories = { new SessionCategoryRecord { Module = "m", Category = "c", ItemsCleaned = 3 } }
        };
        store.Save(scanOnly);
        store.Save(cleaned);

        Assert.Equal(2, store.GetRecent(10).Count);
        Assert.Equal(cleaned.Id, store.GetRecent(1)[0].Id);
        Assert.Equal(1234, store.GetLastCleanup()!.TotalRecoveredBytes);

        var text = ReportExporter.ToText(store.GetRecent(10));
        Assert.Contains("1.21 KB", text);

        store.Clear();
        Assert.Empty(store.GetRecent(10));
    }

    [Fact]
    public void Logger_rotates_redacts_the_user_profile_and_can_be_cleared()
    {
        using var temp = new TempDirectory();
        var layout = new AppDataLayout(temp.Combine("data"));
        layout.EnsureCreated();
        var profile = temp.Combine("Users", "alice");
        using var logger = new FileAppLogger(layout, new LogSanitizer(profile));

        logger.Info($"Cleaning {Path.Combine(profile, "AppData", "x")}");
        logger.Audit("DELETED something");

        var files = Directory.GetFiles(layout.LogsDirectory);
        Assert.Equal(2, files.Length);
        var text = string.Join("\n", files.Select(f => File.ReadAllText(f)));
        Assert.Contains("%USERPROFILE%", text);
        Assert.DoesNotContain("alice", text);

        logger.ClearLogs();
        Assert.Empty(Directory.GetFiles(layout.LogsDirectory));
    }

    [Fact]
    public void Byte_formatting_is_human_readable()
    {
        Assert.Equal("0 MB", ByteFormatter.Format(0));
        Assert.Equal("1 KB", ByteFormatter.Format(1024));
        Assert.Equal("1.24 GB", ByteFormatter.Format((long)(1.24 * 1024 * 1024 * 1024)));
    }
}
