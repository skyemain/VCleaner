using CleanSlate.Cleaners.Registry;
using CleanSlate.Core.Models;
using CleanSlate.TestSupport;
using Xunit;

namespace CleanSlate.UnitTests;

public sealed class CommandTargetResolverTests
{
    private static CommandTargetResolver Create(Action<FakeFileProbe> setup, Action<FakeEnvironment>? env = null)
    {
        var probe = new FakeFileProbe();
        setup(probe);
        var e = new FakeEnvironment();
        env?.Invoke(e);
        return new CommandTargetResolver(probe, e);
    }

    [Fact]
    public void Quoted_existing_path_with_arguments_exists()
    {
        var r = Create(p => p.AddFile(@"C:\Program Files\App\app.exe"));
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand("\"C:\\Program Files\\App\\app.exe\" --minimized /x", false).Status);
    }

    [Fact]
    public void Unquoted_path_with_spaces_and_arguments_is_resolved_like_createprocess()
    {
        var r = Create(p => p.AddFile(@"C:\Program Files\App\app.exe"));
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand(@"C:\Program Files\App\app.exe -silent", false).Status);
    }

    [Fact]
    public void Environment_variables_are_expanded()
    {
        var r = Create(p => p.AddFile(@"C:\Users\tester\AppData\Local\Tool\tool.exe"));
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand("\"%LOCALAPPDATA%\\Tool\\tool.exe\" --bg", false).Status);
    }

    [Fact]
    public void Missing_absolute_executable_on_fixed_drive_is_reported_missing()
    {
        var r = Create(_ => { });
        var result = r.ResolveCommand("\"C:\\Gone\\old.exe\" /auto", false);
        Assert.Equal(TargetStatus.Missing, result.Status);
        Assert.Equal(@"C:\Gone\old.exe", result.Path);
    }

    [Fact]
    public void Extensionless_targets_get_exe_appended_like_windows()
    {
        var r = Create(p => p.AddFile(@"C:\Tools\runner.exe"));
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand(@"C:\Tools\runner --go", false).Status);
    }

    [Theory]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL")]
    [InlineData("cmd /c start \"\" notepad")]
    [InlineData("powershell -File something.ps1")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("shell:AppsFolder\\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("\\\\server\\share\\tool.exe")]
    [InlineData("D:\\removable\\tool.exe")]
    [InlineData("%UNDEFINEDVAR%\\tool.exe")]
    [InlineData("\"C:\\unbalanced\\tool.exe")]
    [InlineData("")]
    public void Anything_that_cannot_be_verified_is_never_reported_missing(string command)
    {
        var r = Create(_ => { });
        Assert.NotEqual(TargetStatus.Missing, r.ResolveCommand(command, false).Status);
    }

    [Fact]
    public void Bare_commands_found_on_the_search_path_exist()
    {
        var r = Create(p => p.AddFile(@"C:\Windows\System32\notepad.exe"));
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand("notepad", false).Status);
    }

    [Fact]
    public void Thirty_two_bit_entries_also_check_program_files_x86_and_syswow64()
    {
        var r = Create(p => p.AddFile(@"C:\Program Files (x86)\Old\old.exe").AddFile(@"C:\Windows\SysWOW64\thing.exe"));
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand("\"%ProgramFiles%\\Old\\old.exe\"", true).Status);
        Assert.Equal(TargetStatus.Missing, r.ResolveCommand("\"%ProgramFiles%\\Old\\old.exe\"", false).Status);
        Assert.Equal(TargetStatus.Exists, r.ResolveCommand("%SystemRoot%\\System32\\thing.exe", true).Status);
    }
}

public sealed class RegistryScannerTests
{
    private static readonly RegistryKeyRef Run = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\CleanSlateTest\Run");
    private static readonly RegistryKeyRef RunLm = new(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\CleanSlateTest\Run");
    private static readonly RegistryKeyRef AppPaths = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\CleanSlateTest\App Paths");
    private static readonly RegistryKeyRef Uninstall = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\CleanSlateTest\Uninstall");

    public static RegistryScanLocations Locations { get; } = new()
    {
        StartupKeys = new[] { Run, RunLm },
        AppPathsKeys = new[] { AppPaths },
        UninstallKeys = new[] { Uninstall }
    };

    [Fact]
    public void Detects_missing_startup_targets_and_ignores_valid_or_unverifiable_ones()
    {
        using var h = new Harness(Locations);
        h.RegistryProbe.AddFile(@"C:\Program Files\Good\good.exe").AddFile(@"C:\Windows\System32\rundll32.exe");
        h.Registry.Seed(Run, "Good", RegistryData.String("\"C:\\Program Files\\Good\\good.exe\" /tray"));
        h.Registry.Seed(Run, "Gone", RegistryData.String("\"C:\\Program Files\\Gone\\gone.exe\" /tray"));
        h.Registry.Seed(Run, "Dll", RegistryData.String("rundll32.exe foo.dll,Entry"));
        h.Registry.Seed(Run, "Removable", RegistryData.String(@"D:\usb\tool.exe"));
        h.Registry.Seed(Run, "Binary", new RegistryData(RegistryDataKind.Binary, new byte[] { 1, 2 }));

        var result = h.Scanner.Scan(CancellationToken.None);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("Gone", issue.ValueName);
        Assert.Equal(RegistryRuleKind.StartupEntry, issue.Rule);
        Assert.Equal(RegistryRepairAction.DeleteValue, issue.Action);
        Assert.Equal(IssueConfidence.High, issue.Confidence);
        Assert.Contains("gone.exe", issue.Evidence);
        Assert.False(issue.RequiresElevation);
        Assert.Equal(5, result.EntriesExamined);
    }

    [Fact]
    public void Scanning_never_writes_to_the_registry()
    {
        using var h = new Harness(Locations);
        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        h.Scanner.Scan(CancellationToken.None);
        Assert.Equal(0, h.Registry.WriteCount);
    }

    [Fact]
    public void Machine_wide_entries_require_elevation_to_repair()
    {
        using var h = new Harness(Locations);
        h.Registry.Seed(RunLm, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);
        Assert.True(issue.RequiresElevation);
    }

    [Fact]
    public void App_paths_with_verified_missing_targets_are_detected_but_windows_owned_entries_are_not()
    {
        using var h = new Harness(Locations);
        h.RegistryProbe.AddFile(@"C:\Apps\fine\fine.exe");
        h.Registry.Seed(AppPaths.Child("fine.exe"), "", RegistryData.String(@"C:\Apps\fine\fine.exe"));
        h.Registry.Seed(AppPaths.Child("gone.exe"), "", RegistryData.String(@"C:\Apps\gone\gone.exe"));
        h.Registry.Seed(AppPaths.Child("winowned.exe"), "", RegistryData.String(@"C:\Windows\System32\nothere.exe"));
        h.Registry.Seed(AppPaths.Child("nodefault.exe"), "Path", RegistryData.String(@"C:\Apps\x"));
        h.Registry.Seed(AppPaths.Child("notexe.txt"), "", RegistryData.String(@"C:\Apps\gone\gone.txt"));
        h.Registry.Seed(AppPaths.Child("haschildren.exe"), "", RegistryData.String(@"C:\Apps\gone\gone2.exe"));
        h.Registry.SeedKey(AppPaths.Child("haschildren.exe").Child("child"));

        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);
        Assert.Equal(RegistryRuleKind.AppPathsEntry, issue.Rule);
        Assert.Equal(RegistryRepairAction.DeleteKey, issue.Action);
        Assert.EndsWith("gone.exe", issue.Key.Path);
        Assert.Null(issue.ValueName);
    }

    [Fact]
    public void Uninstall_entries_are_only_flagged_with_overwhelming_evidence()
    {
        using var h = new Harness(Locations);
        h.RegistryProbe.AddFile(@"C:\Present\unins000.exe");

        void Entry(string name, string display, string uninstall, params (string, RegistryData)[] extra)
        {
            var key = Uninstall.Child(name);
            h.Registry.Seed(key, "DisplayName", RegistryData.String(display));
            h.Registry.Seed(key, "UninstallString", RegistryData.String(uninstall));
            foreach (var (n, d) in extra)
            {
                h.Registry.Seed(key, n, d);
            }
        }

        Entry("Orphan", "Orphan App", @"""C:\Gone\unins000.exe""");
        Entry("StillThere", "Present App", @"""C:\Present\unins000.exe""");
        Entry("Msi", "Msi App", @"MsiExec.exe /X{ABC}");
        Entry("MsiFlag", "Msi Flag", @"""C:\Gone\x.exe""", ("WindowsInstaller", new RegistryData(RegistryDataKind.DWord, 1)));
        Entry("SysComp", "System Component", @"""C:\Gone\y.exe""", ("SystemComponent", new RegistryData(RegistryDataKind.DWord, 1)));
        Entry("HasLocation", "Has Location", @"""C:\Gone\z.exe""", ("InstallLocation", RegistryData.String(@"C:\Present")));
        Entry("Patch", "Update", @"""C:\Gone\p.exe""", ("ParentKeyName", RegistryData.String("Parent")));
        h.RegistryProbe.AddDirectory(@"C:\Present");
        h.Registry.Seed(Uninstall.Child("NoName"), "UninstallString", RegistryData.String(@"""C:\Gone\n.exe"""));

        var issues = h.Scanner.Scan(CancellationToken.None).Issues;

        var issue = Assert.Single(issues);
        Assert.EndsWith("Orphan", issue.Key.Path);
        Assert.Equal(RegistryRuleKind.OrphanedUninstallEntry, issue.Rule);
        Assert.Equal(RiskLevel.Medium, issue.Risk);
    }

    [Fact]
    public void Revalidation_rejects_entries_that_changed_or_became_valid()
    {
        using var h = new Harness(Locations);
        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);

        Assert.NotNull(h.Scanner.Revalidate(issue));

        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Elsewhere\other.exe"));
        Assert.Null(h.Scanner.Revalidate(issue));

        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        h.RegistryProbe.AddFile(@"C:\Gone\gone.exe");
        Assert.Null(h.Scanner.Revalidate(issue));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Classes\CLSID\{123}")]
    [InlineData(@"SYSTEM\CurrentControlSet\Services\Foo")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs")]
    [InlineData(@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.txt")]
    public void Unsupported_locations_are_never_valid_repair_targets(string path)
    {
        using var h = new Harness(RegistryScanLocations.Production);
        foreach (var rule in Enum.GetValues<RegistryRuleKind>())
        {
            foreach (var action in Enum.GetValues<RegistryRepairAction>())
            {
                foreach (var hive in Enum.GetValues<RegistryHiveKind>())
                {
                    var key = new RegistryKeyRef(hive, RegistryViewKind.Registry64, path);
                    Assert.False(h.Scanner.IsSupportedTarget(rule, key, "Value", action));
                    Assert.False(h.Scanner.IsSupportedTarget(rule, key, null, action));
                }
            }
        }
    }

    [Fact]
    public void Production_locations_are_the_only_supported_targets()
    {
        using var h = new Harness(RegistryScanLocations.Production);
        var run = RegistryScanLocations.Production.StartupKeys[0];
        Assert.True(h.Scanner.IsSupportedTarget(RegistryRuleKind.StartupEntry, run, "Foo", RegistryRepairAction.DeleteValue));
        Assert.False(h.Scanner.IsSupportedTarget(RegistryRuleKind.StartupEntry, run, "Foo", RegistryRepairAction.DeleteKey));
        Assert.False(h.Scanner.IsSupportedTarget(RegistryRuleKind.StartupEntry, run, null, RegistryRepairAction.DeleteValue));
        Assert.False(h.Scanner.IsSupportedTarget(RegistryRuleKind.StartupEntry, run with { Path = run.Path + @"\Sub" }, "Foo", RegistryRepairAction.DeleteValue));
        var appPaths = RegistryScanLocations.Production.AppPathsKeys[0];
        Assert.True(h.Scanner.IsSupportedTarget(RegistryRuleKind.AppPathsEntry, appPaths.Child("foo.exe"), null, RegistryRepairAction.DeleteKey));
        Assert.False(h.Scanner.IsSupportedTarget(RegistryRuleKind.AppPathsEntry, appPaths, null, RegistryRepairAction.DeleteKey));
    }
}

public sealed class RegistryRepairTests
{
    private static readonly RegistryKeyRef Run = RegistryScannerTests.Locations.StartupKeys[0];
    private static readonly RegistryKeyRef RunLm = RegistryScannerTests.Locations.StartupKeys[1];
    private static readonly RegistryKeyRef AppPaths = RegistryScannerTests.Locations.AppPathsKeys[0];

    [Fact]
    public async Task Repair_backs_up_verifies_and_removes_only_the_single_value()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        h.Registry.Seed(Run, "Other", RegistryData.String(@"C:\Gone\other.exe"));
        var issue = h.Scanner.Scan(CancellationToken.None).Issues.Single(i => i.ValueName == "Gone");

        var report = await h.Repair.RepairAsync(new[] { issue }, createRestorePoint: true, null, CancellationToken.None);

        var item = Assert.Single(report.Items);
        Assert.Equal(RegistryRepairStatus.Repaired, item.Status);
        Assert.NotNull(item.BackupId);
        Assert.Null(h.Registry.GetValue(Run, "Gone"));
        Assert.NotNull(h.Registry.GetValue(Run, "Other"));
        Assert.True(h.Registry.KeyExists(Run));
        Assert.True(File.Exists(Path.Combine(h.AppData.RegistryBackupsDirectory, item.BackupId + ".json")));
        Assert.True(report.RestorePointCreated);
        Assert.Equal(1, h.RestorePoint.Calls);
        Assert.Contains(h.Log.Lines, l => l.StartsWith("AUDIT REGISTRY REPAIRED") && l.Contains(item.BackupId!));
    }

    [Fact]
    public async Task Restore_point_is_best_effort_and_optional()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        h.RestorePoint.Succeed = false;
        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);

        var report = await h.Repair.RepairAsync(new[] { issue }, createRestorePoint: false, null, CancellationToken.None);

        Assert.Equal(0, h.RestorePoint.Calls);
        Assert.Equal(1, report.Repaired);

        h.Registry.Seed(Run, "Gone2", RegistryData.String(@"C:\Gone\gone2.exe"));
        var issue2 = h.Scanner.Scan(CancellationToken.None).Issues.Single();
        var report2 = await h.Repair.RepairAsync(new[] { issue2 }, createRestorePoint: true, null, CancellationToken.None);
        Assert.False(report2.RestorePointCreated);
        Assert.Equal(1, report2.Repaired);
    }

    [Fact]
    public async Task Backup_failure_means_the_registry_is_not_modified()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        Directory.Delete(h.AppData.RegistryBackupsDirectory, true);
        File.WriteAllText(h.AppData.RegistryBackupsDirectory, "this is a file, so backups cannot be written");
        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);

        var report = await h.Repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);

        Assert.Equal(RegistryRepairStatus.BackupFailed, report.Items.Single().Status);
        Assert.NotNull(h.Registry.GetValue(Run, "Gone"));
        Assert.Equal(0, h.Registry.WriteCount);
    }

    [Fact]
    public async Task Entries_that_changed_after_the_scan_are_left_alone()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        h.Registry.Seed(Run, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);
        h.RegistryProbe.AddFile(@"C:\Gone\gone.exe");

        var report = await h.Repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);

        Assert.Equal(RegistryRepairStatus.SkippedNoLongerInvalid, report.Items.Single().Status);
        Assert.NotNull(h.Registry.GetValue(Run, "Gone"));
    }

    [Fact]
    public async Task Unsupported_targets_are_rejected_and_never_modified()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        var clsid = new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Classes\CLSID\{1}");
        h.Registry.Seed(clsid, "Name", RegistryData.String(@"C:\Gone\gone.exe"));
        var forged = new RegistryIssue
        {
            Id = "forged",
            Rule = RegistryRuleKind.StartupEntry,
            RuleTitle = "Forged",
            Key = clsid,
            ValueName = "Name",
            Reason = "x",
            Evidence = "x",
            Confidence = IssueConfidence.High,
            Risk = RiskLevel.Low,
            Action = RegistryRepairAction.DeleteValue,
            ProposedAction = "x"
        };

        var report = await h.Repair.RepairAsync(new[] { forged }, false, null, CancellationToken.None);

        Assert.Equal(RegistryRepairStatus.SkippedNotSupported, report.Items.Single().Status);
        Assert.NotNull(h.Registry.GetValue(clsid, "Name"));
        Assert.Equal(0, h.Registry.WriteCount);
    }

    [Fact]
    public async Task Machine_wide_repairs_require_elevation()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        h.Registry.Seed(RunLm, "Gone", RegistryData.String(@"C:\Gone\gone.exe"));
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);

        var denied = await h.Repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);
        Assert.Equal(RegistryRepairStatus.SkippedNeedsElevation, denied.Items.Single().Status);
        Assert.NotNull(h.Registry.GetValue(RunLm, "Gone"));

        h.Privilege.IsElevated = true;
        var allowed = await h.Repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);
        Assert.Equal(RegistryRepairStatus.Repaired, allowed.Items.Single().Status);
    }

    [Fact]
    public async Task Backups_can_be_restored_and_restore_never_overwrites_different_data()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        var original = RegistryData.ExpandString(@"%ProgramFiles%\Gone\gone.exe --x");
        h.Registry.Seed(Run, "Gone", original);
        var issue = Assert.Single(h.Scanner.Scan(CancellationToken.None).Issues);
        var report = await h.Repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);
        var backupId = report.Items.Single().BackupId!;
        Assert.Null(h.Registry.GetValue(Run, "Gone"));
        Assert.Contains(h.Repair.ListBackups(), b => b.BackupId == backupId && b.IsValid);

        var restored = h.Repair.Restore(backupId);

        Assert.True(restored.Success, restored.Message);
        var back = h.Registry.GetValue(Run, "Gone");
        Assert.NotNull(back);
        Assert.Equal(RegistryDataKind.ExpandString, back!.Kind);
        Assert.True(original.ValueEquals(back));

        h.Registry.Seed(Run, "Gone", RegistryData.String("changed"));
        var second = h.Repair.Restore(backupId);
        Assert.False(second.Success);
        Assert.Equal("changed", h.Registry.GetValue(Run, "Gone")!.AsText);
    }

    [Fact]
    public async Task Key_level_repairs_back_up_every_value_and_restore_recreates_the_key()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        var key = AppPaths.Child("gone.exe");
        h.Registry.Seed(key, "", RegistryData.String(@"C:\Gone\gone.exe"));
        h.Registry.Seed(key, "Path", RegistryData.String(@"C:\Gone"));
        h.Registry.Seed(key, "Flags", new RegistryData(RegistryDataKind.DWord, 7));
        h.Registry.Seed(key, "Blob", new RegistryData(RegistryDataKind.Binary, new byte[] { 1, 2, 3 }));
        h.Registry.Seed(key, "Multi", new RegistryData(RegistryDataKind.MultiString, new[] { "a", "b" }));
        h.Registry.Seed(key, "Big", new RegistryData(RegistryDataKind.QWord, 1L << 40));
        h.Registry.Seed(AppPaths.Child("other.exe"), "", RegistryData.String(@"C:\Elsewhere\o.exe"));
        var issue = h.Scanner.Scan(CancellationToken.None).Issues.Single(i => i.Key.Path.EndsWith("gone.exe"));

        var report = await h.Repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);
        Assert.Equal(RegistryRepairStatus.Repaired, report.Items.Single().Status);
        Assert.False(h.Registry.KeyExists(key));
        Assert.True(h.Registry.KeyExists(AppPaths));

        Assert.True(h.Repair.Restore(report.Items.Single().BackupId!).Success);
        Assert.Equal(6, h.Registry.GetValueNames(key).Count);
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])h.Registry.GetValue(key, "Blob")!.Value);
        Assert.Equal(new[] { "a", "b" }, (string[])h.Registry.GetValue(key, "Multi")!.Value);
        Assert.Equal(1L << 40, (long)h.Registry.GetValue(key, "Big")!.Value);
    }

    [Fact]
    public void Tampered_backups_pointing_at_unsupported_locations_are_not_restored()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        var doc = new RegistryBackupDocument
        {
            BackupId = "tampered",
            RuleKind = nameof(RegistryRuleKind.StartupEntry),
            Hive = RegistryHiveKind.CurrentUser,
            KeyPath = @"Software\Classes\CLSID\{1}",
            ValueName = "x",
            Values = { new RegistryBackupEntry("x", RegistryDataKind.String, "evil") }
        };
        File.WriteAllText(Path.Combine(h.AppData.RegistryBackupsDirectory, "tampered.json"), System.Text.Json.JsonSerializer.Serialize(doc));

        var result = h.Repair.Restore("tampered");

        Assert.False(result.Success);
        Assert.Equal(0, h.Registry.WriteCount);
        Assert.False(h.Repair.Restore("..\\..\\evil").Success);
    }

    [Fact]
    public async Task Concurrent_repairs_are_blocked_by_the_run_gate()
    {
        using var h = new Harness(RegistryScannerTests.Locations);
        using var lease = h.Gate.Enter();
        await Assert.ThrowsAsync<CleanSlate.Core.Services.EngineBusyException>(() =>
            h.Repair.RepairAsync(Array.Empty<RegistryIssue>(), false, null, CancellationToken.None));
    }
}
