using Clenzy.Cleaners.Browsers;
using Clenzy.Core.Models;
using Clenzy.Core.Services;
using Clenzy.TestSupport;
using Xunit;

namespace Clenzy.UnitTests;

public sealed class BrowserTests
{
    private const string ChromeData = "Users/tester/AppData/Local/Google/Chrome/User Data";
    private const string EdgeData = "Users/tester/AppData/Local/Microsoft/Edge/User Data";

    private static void BuildProfile(Harness h, string userData, string profile, string displayName)
    {
        var p = $"{userData}/{profile}";
        h.Temp.CreateFile($"{p}/Preferences", 50);
        h.Temp.CreateFile($"{p}/Bookmarks", 60);
        h.Temp.CreateFile($"{p}/Login Data", 70);
        h.Temp.CreateFile($"{p}/Web Data", 71);
        h.Temp.CreateFile($"{p}/History", 72);
        h.Temp.CreateFile($"{p}/Network/Cookies", 73);
        h.Temp.CreateFile($"{p}/Extensions/abc/manifest.json", 74);
        h.Temp.CreateFile($"{p}/Local Extension Settings/abc/000003.log", 75);
        h.Temp.CreateFile($"{p}/Cache/Cache_Data/data_0", 1000);
        h.Temp.CreateFile($"{p}/Cache/Cache_Data/f_000001", 2000);
        h.Temp.CreateFile($"{p}/Code Cache/js/index", 300);
        h.Temp.CreateFile($"{p}/GPUCache/data_1", 400);
        h.Temp.CreateFile($"{p}/Local Storage/leveldb/000003.log", 500);
        h.Temp.CreateFile($"{p}/IndexedDB/site/data.ldb", 600);
        WriteLocalState(h, userData, (profile, displayName));
    }

    private static void WriteLocalState(Harness h, string userData, params (string Profile, string Name)[] names)
    {
        var entries = string.Join(",", names.Select(n => $"\"{n.Profile}\":{{\"name\":\"{n.Name}\"}}"));
        File.WriteAllText(h.Temp.Combine(userData, "Local State"), $"{{\"profile\":{{\"info_cache\":{{{entries}}}}}}}");
    }

    private static string[] Preserved(string userData, string profile) => new[]
    {
        $"{userData}/{profile}/Preferences", $"{userData}/{profile}/Bookmarks", $"{userData}/{profile}/Login Data",
        $"{userData}/{profile}/Web Data", $"{userData}/{profile}/History", $"{userData}/{profile}/Network/Cookies",
        $"{userData}/{profile}/Extensions/abc/manifest.json", $"{userData}/{profile}/Local Extension Settings/abc/000003.log"
    };

    private static async Task<(ModuleScanResult Scan, ModuleCleanResult Clean)> RunAsync(Harness h, string moduleId, Clenzy.Core.Configuration.ClenzySettings settings)
    {
        var module = h.Module(moduleId);
        var scan = await module.ScanAsync(new Clenzy.Core.Interfaces.ScanContext
        {
            Settings = settings,
            Reporter = Clenzy.Core.Interfaces.NullModuleReporter.Instance,
            Claims = new ClaimSet(),
            Exclusions = ExclusionPolicy.None
        }, CancellationToken.None);

        var clean = await module.CleanAsync(new Clenzy.Core.Interfaces.CleanContext
        {
            Settings = settings,
            Reporter = Clenzy.Core.Interfaces.NullModuleReporter.Instance,
            Exclusions = ExclusionPolicy.None
        }, scan.Findings.Where(f => f.Action == ActionType.DeleteFile).ToList(), CancellationToken.None);
        return (scan, clean);
    }

    [Fact]
    public void Profile_discovery_finds_multiple_profiles_with_display_names_and_ignores_incomplete_ones()
    {
        using var h = new Harness();
        BuildProfile(h, ChromeData, "Default", "Personal");
        BuildProfile(h, ChromeData, "Profile 1", "Work");
        WriteLocalState(h, ChromeData, ("Default", "Personal"), ("Profile 1", "Work"));
        Directory.CreateDirectory(h.Temp.Combine(ChromeData, "Profile 2"));
        Directory.CreateDirectory(h.Temp.Combine(ChromeData, "Crashpad"));
        Directory.CreateDirectory(h.Temp.Combine(ChromeData, "Guest Profile"));

        var profiles = ChromiumProfileDiscovery.Discover(h.Temp.Combine(ChromeData), out var notes);

        Assert.Equal(new[] { "Default", "Profile 1" }, profiles.Select(p => p.DirectoryName));
        Assert.Contains(profiles, p => p.DirectoryName == "Default" && p.DisplayName == "Personal");
        Assert.Contains(notes, n => n.Contains("Profile 2"));
    }

    [Fact]
    public async Task Chrome_cache_is_cleaned_for_every_profile_and_personal_data_is_preserved()
    {
        using var h = new Harness();
        BuildProfile(h, ChromeData, "Default", "Personal");
        BuildProfile(h, ChromeData, "Profile 1", "Work");
        h.Temp.CreateFile($"{ChromeData}/Crashpad/reports/report1.dmp", 90);
        var settings = Harness.Minimal(s => s.CleanChromeCache = true);

        var (scan, clean) = await RunAsync(h, "browser.chrome", settings);

        Assert.Equal(ModuleScanStatus.Completed, scan.Status);
        var categories = scan.Findings.Select(f => f.Category).Distinct().ToHashSet();
        Assert.Contains("Disk cache", categories);
        Assert.Contains("Code cache", categories);
        Assert.Contains("GPU cache", categories);
        Assert.Contains("Temporary browser artifacts", categories);
        Assert.DoesNotContain("Site storage", categories);
        Assert.All(scan.Findings.Where(f => f.Action == ActionType.DeleteFile), f => Assert.True(f.AutoCleanAllowed && !f.ConfirmationRequired));

        Assert.Equal(0, clean.Errors);
        Assert.Equal((1000 + 2000 + 300 + 400) * 2 + 90, clean.BytesRecovered);
        foreach (var profile in new[] { "Default", "Profile 1" })
        {
            Assert.False(File.Exists(h.Temp.Combine(ChromeData, profile, "Cache", "Cache_Data", "data_0")));
            Assert.False(File.Exists(h.Temp.Combine(ChromeData, profile, "GPUCache", "data_1")));
            Assert.True(File.Exists(h.Temp.Combine(ChromeData, profile, "Local Storage", "leveldb", "000003.log")));
            Assert.True(File.Exists(h.Temp.Combine(ChromeData, profile, "IndexedDB", "site", "data.ldb")));
            foreach (var kept in Preserved(ChromeData, profile))
            {
                Assert.True(File.Exists(h.Temp.Combine(kept)), kept);
            }
        }
    }

    [Fact]
    public async Task Edge_is_scanned_independently_of_chrome()
    {
        using var h = new Harness();
        BuildProfile(h, EdgeData, "Default", "Profile 1");
        BuildProfile(h, ChromeData, "Default", "Personal");
        var settings = Harness.Minimal(s => s.CleanEdgeCache = true);

        var (scan, _) = await RunAsync(h, "browser.edge", settings);

        Assert.All(scan.Findings.Where(f => f.Action == ActionType.DeleteFile), f => Assert.Contains("Edge", f.Location));
        Assert.Equal(CleanerGroup.Edge, scan.Group);
        Assert.True(File.Exists(h.Temp.Combine(ChromeData, "Default", "Cache", "Cache_Data", "data_0")));

        var chrome = await h.Module("browser.chrome").ScanAsync(new Clenzy.Core.Interfaces.ScanContext
        {
            Settings = Harness.Minimal(),
            Reporter = Clenzy.Core.Interfaces.NullModuleReporter.Instance,
            Claims = new ClaimSet(),
            Exclusions = ExclusionPolicy.None
        }, CancellationToken.None);
        Assert.False(h.Module("browser.chrome").IsEnabled(Harness.Minimal()));
        Assert.DoesNotContain(chrome.Findings, f => f.Action == ActionType.DeleteFile);
    }

    [Fact]
    public async Task A_running_browser_skips_categories_that_need_it_closed_without_touching_files()
    {
        using var h = new Harness();
        BuildProfile(h, EdgeData, "Default", "Default");
        h.Temp.CreateFile($"{EdgeData}/Crashpad/reports/r.dmp", 10);
        h.Browser.Running = true;
        var before = Harness.SnapshotTree(h.Temp.Combine(EdgeData));

        var (scan, clean) = await RunAsync(h, "browser.edge", Harness.Minimal(s => s.CleanEdgeCache = true));

        var skipped = scan.Findings.Where(f => f.Action == ActionType.None).ToList();
        Assert.Equal(3, skipped.Count);
        Assert.All(skipped, f =>
        {
            Assert.False(f.IsEligible);
            Assert.StartsWith("Skipped: Microsoft Edge is running.", f.SkipReason);
        });
        Assert.Contains("Startup Boost", skipped[0].SkipReason);

        var cleaned = scan.Findings.Where(f => f.Action == ActionType.DeleteFile).ToList();
        Assert.Single(cleaned);
        Assert.Contains("Crashpad", cleaned[0].Location);
        Assert.Equal(1, clean.ItemsCleaned);
        Assert.False(File.Exists(h.Temp.Combine(EdgeData, "Crashpad", "reports", "r.dmp")));
        var after = Harness.SnapshotTree(h.Temp.Combine(EdgeData));
        Assert.Equal(before.Split('\n').Length - 1, after.Split('\n').Length);
        Assert.True(File.Exists(h.Temp.Combine(EdgeData, "Default", "Cache", "Cache_Data", "data_0")));
    }

    [Fact]
    public async Task A_running_browser_is_closed_then_cleaned_and_sign_in_data_is_preserved()
    {
        using var h = new Harness();
        BuildProfile(h, EdgeData, "Default", "Default");
        BuildProfile(h, EdgeData, "Profile 1", "Work");
        h.Browser.Running = true;
        var settings = Harness.Minimal(s =>
        {
            s.CleanEdgeCache = true;
            s.CloseBrowsersBeforeCleaning = true;
        });

        var (scan, clean) = await RunAsync(h, "browser.edge", settings);

        Assert.Equal(1, h.Closer.CloseCalls);
        Assert.Equal(new[] { "edge" }, h.Closer.Closed);
        Assert.DoesNotContain(scan.Findings, f => f.Action == ActionType.None);
        Assert.False(File.Exists(h.Temp.Combine(EdgeData, "Default", "Cache", "Cache_Data", "data_0")));
        Assert.False(File.Exists(h.Temp.Combine(EdgeData, "Profile 1", "GPUCache", "data_1")));
        Assert.Equal(0, clean.Errors);
        foreach (var profile in new[] { "Default", "Profile 1" })
        {
            foreach (var kept in Preserved(EdgeData, profile))
            {
                Assert.True(File.Exists(h.Temp.Combine(kept)), kept);
            }
        }

        Assert.True(File.Exists(h.Temp.Combine(EdgeData, "Default", "Local Storage", "leveldb", "000003.log")));
        Assert.Contains(h.Log.Lines, e => e.Contains("CLOSED-BROWSER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_browser_is_not_closed_when_it_is_not_running_or_the_option_is_off_or_nothing_is_to_clean()
    {
        using var h = new Harness();
        BuildProfile(h, EdgeData, "Default", "Default");

        await RunAsync(h, "browser.edge", Harness.Minimal(s => { s.CleanEdgeCache = true; s.CloseBrowsersBeforeCleaning = true; }));
        Assert.Equal(0, h.Closer.CloseCalls);

        h.Browser.Running = true;
        await RunAsync(h, "browser.edge", Harness.Minimal(s => { s.CleanEdgeCache = true; s.CloseBrowsersBeforeCleaning = false; }));
        Assert.Equal(0, h.Closer.CloseCalls);

        await RunAsync(h, "browser.edge", Harness.Minimal(s => { s.CleanEdgeCache = false; s.CloseBrowsersBeforeCleaning = true; }));
        Assert.Equal(0, h.Closer.CloseCalls);
    }

    [Fact]
    public async Task A_browser_that_cannot_be_closed_is_reported_and_locked_files_are_skipped_safely()
    {
        using var h = new Harness();
        BuildProfile(h, ChromeData, "Default", "Default");
        h.Browser.Running = true;
        h.Closer.CannotClose = true;
        var locked = h.Temp.Combine(ChromeData, "Default", "Cache", "Cache_Data", "data_0");

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (_, clean) = await RunAsync(h, "browser.chrome", Harness.Minimal(s => { s.CleanChromeCache = true; s.CloseBrowsersBeforeCleaning = true; }));

        Assert.Equal(1, h.Closer.CloseCalls);
        Assert.True(File.Exists(locked));
        Assert.Equal(1, clean.ItemsSkipped);
        Assert.Contains(h.Log.Lines, e => e.Contains("could not be fully closed", StringComparison.Ordinal));
        foreach (var kept in Preserved(ChromeData, "Default"))
        {
            Assert.True(File.Exists(h.Temp.Combine(kept)), kept);
        }
    }

    [Fact]
    public void The_inventory_names_only_running_enabled_browsers_when_closing_is_allowed()
    {
        using var h = new Harness();
        BuildProfile(h, EdgeData, "Default", "Default");
        BuildProfile(h, ChromeData, "Default", "Default");
        var inventory = new BrowserInventory(h.Paths, h.Browser);
        var settings = Harness.Minimal(s => { s.CleanEdgeCache = true; s.CleanChromeCache = true; s.CloseBrowsersBeforeCleaning = true; });

        Assert.Empty(inventory.GetBrowsersThatWouldBeClosed(settings));

        h.Browser.Running = true;
        Assert.Equal(new[] { "Google Chrome", "Microsoft Edge" }, inventory.GetBrowsersThatWouldBeClosed(settings));

        settings.CleanChromeCache = false;
        Assert.Equal(new[] { "Microsoft Edge" }, inventory.GetBrowsersThatWouldBeClosed(settings));

        settings.CloseBrowsersBeforeCleaning = false;
        Assert.Empty(inventory.GetBrowsersThatWouldBeClosed(settings));
    }

    [Fact]
    public void The_process_closer_ends_a_stubborn_browser_process_and_ignores_unrelated_ones()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var temp = new TempDirectory();
        var dir = temp.Combine("cs-fake-browser-dir");
        Directory.CreateDirectory(dir);
        var fake = Path.Combine(dir, "csfakebrowser");
        File.Copy(File.Exists("/bin/sleep") ? "/bin/sleep" : "/usr/bin/sleep", fake);
        File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var target = System.Diagnostics.Process.Start(fake, "120")!;
        using var bystander = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sleep", "120"))!;
        try
        {
            var descriptor = new BrowserDescriptor("fake", "Fake Browser", CleanerGroup.Chrome, "x", new[] { "csfakebrowser" },
                @"\cs-fake-browser-dir\", Array.Empty<string>(), "close it");
            var closer = new ProcessBrowserCloser { GracePeriod = TimeSpan.FromMilliseconds(400), SettleTime = TimeSpan.FromMilliseconds(100) };

            var result = closer.Close(descriptor, CancellationToken.None);

            Assert.Equal(1, result.ProcessesFound);
            Assert.True(result.IsClosed);
            Assert.True(result.ForcedAny);
            Assert.True(target.WaitForExit(3000));
            Assert.False(bystander.HasExited);
            Assert.Equal(BrowserCloseResult.NotRunning, closer.Close(descriptor, CancellationToken.None));
        }
        finally
        {
            if (!bystander.HasExited)
            {
                bystander.Kill();
            }

            if (!target.HasExited)
            {
                target.Kill();
            }
        }
    }

    [Fact]
    public async Task Locked_cache_files_are_skipped_and_other_files_still_cleaned()
    {
        using var h = new Harness();
        BuildProfile(h, ChromeData, "Default", "Default");
        var settings = Harness.Minimal(s => s.CleanChromeCache = true);
        var locked = h.Temp.Combine(ChromeData, "Default", "Cache", "Cache_Data", "data_0");

        using var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (_, clean) = await RunAsync(h, "browser.chrome", settings);

        Assert.Equal(1, clean.ItemsSkipped);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(h.Temp.Combine(ChromeData, "Default", "Cache", "Cache_Data", "f_000001")));
        Assert.Contains(clean.Outcomes, o => o.Message!.Contains("in use", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Site_storage_is_opt_in_confirmation_required_and_keeps_extension_data()
    {
        using var h = new Harness();
        BuildProfile(h, ChromeData, "Default", "Default");
        var settings = Harness.Minimal(s => s.ClearBrowserSiteStorage = true);

        var (scan, clean) = await RunAsync(h, "browser.chrome", settings);

        var findings = scan.Findings.Where(f => f.Action == ActionType.DeleteFile).ToList();
        Assert.All(findings, f =>
        {
            Assert.Equal("Site storage", f.Category);
            Assert.True(f.ConfirmationRequired);
            Assert.False(f.AutoCleanAllowed);
            Assert.Equal(RiskLevel.Medium, f.Risk);
        });
        Assert.Equal(500 + 600, clean.BytesRecovered);
        Assert.True(File.Exists(h.Temp.Combine(ChromeData, "Default", "Local Extension Settings", "abc", "000003.log")));
        Assert.True(File.Exists(h.Temp.Combine(ChromeData, "Default", "Cache", "Cache_Data", "data_0")));
    }

    [Fact]
    public async Task Missing_browser_is_reported_as_unavailable()
    {
        using var h = new Harness();
        var scan = await h.Module("browser.chrome").ScanAsync(new Clenzy.Core.Interfaces.ScanContext
        {
            Settings = Harness.Minimal(s => s.CleanChromeCache = true),
            Reporter = Clenzy.Core.Interfaces.NullModuleReporter.Instance,
            Claims = new ClaimSet(),
            Exclusions = ExclusionPolicy.None
        }, CancellationToken.None);

        Assert.Equal(ModuleScanStatus.Unavailable, scan.Status);
        Assert.Contains("not installed", scan.Message);
    }

    [Fact]
    public void History_and_cookie_cleaning_are_reported_unsupported_and_cannot_be_enabled()
    {
        var settings = new Clenzy.Core.Configuration.ClenzySettings { ClearBrowserHistory = true, ClearBrowserCookies = true };
        settings.Normalize();
        Assert.False(settings.ClearBrowserHistory);
        Assert.False(settings.ClearBrowserCookies);
        Assert.False(settings.Clone().ClearBrowserCookies);
        Assert.Contains("SQLite", BrowserCapabilities.HistoryReason);
        Assert.Contains("SQLite", BrowserCapabilities.CookiesReason);
    }
}
