using CleanSlate.Cleaners.Registry;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.TestSupport;
using CleanSlate.Windows;
using Microsoft.Win32;
using Xunit;

namespace CleanSlate.WindowsTests;

/// <summary>Creates an isolated HKCU key that is removed afterwards. Never touches real application keys.</summary>
public sealed class IsolatedKey : IDisposable
{
    public IsolatedKey()
    {
        Path = @"Software\CleanSlateTests\" + Guid.NewGuid().ToString("N");
        Ref = new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, Path);
    }

    public string Path { get; }

    public RegistryKeyRef Ref { get; }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
            using var parent = Registry.CurrentUser.OpenSubKey(@"Software\CleanSlateTests", true);
            if (parent is { SubKeyCount: 0 })
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\CleanSlateTests", throwOnMissingSubKey: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
    }
}

public sealed class WindowsRegistryAccessTests
{
    private readonly WindowsRegistryAccess _registry = new();

    [Fact]
    public void Values_round_trip_with_their_raw_kinds_and_expand_strings_stay_unexpanded()
    {
        using var iso = new IsolatedKey();
        _registry.CreateKey(iso.Ref);

        _registry.SetValue(iso.Ref, "s", RegistryData.String("text"));
        _registry.SetValue(iso.Ref, "e", RegistryData.ExpandString(@"%SystemRoot%\x.exe"));
        _registry.SetValue(iso.Ref, "d", new RegistryData(RegistryDataKind.DWord, 7));
        _registry.SetValue(iso.Ref, "b", new RegistryData(RegistryDataKind.Binary, new byte[] { 1, 2, 3 }));
        _registry.SetValue(iso.Ref, "m", new RegistryData(RegistryDataKind.MultiString, new[] { "a", "b" }));
        _registry.SetValue(iso.Ref, "", RegistryData.String("default"));

        Assert.Equal("text", _registry.GetValue(iso.Ref, "s")!.AsText);
        var expand = _registry.GetValue(iso.Ref, "e")!;
        Assert.Equal(RegistryDataKind.ExpandString, expand.Kind);
        Assert.Equal(@"%SystemRoot%\x.exe", expand.AsText);
        Assert.Equal(7, _registry.GetValue(iso.Ref, "d")!.Value);
        Assert.True(new RegistryData(RegistryDataKind.Binary, new byte[] { 1, 2, 3 }).ValueEquals(_registry.GetValue(iso.Ref, "b")));
        Assert.True(new RegistryData(RegistryDataKind.MultiString, new[] { "a", "b" }).ValueEquals(_registry.GetValue(iso.Ref, "m")));
        Assert.Contains("", _registry.GetValueNames(iso.Ref));
        Assert.Null(_registry.GetValue(iso.Ref, "missing"));
    }

    [Fact]
    public void Missing_keys_are_reported_as_missing_not_as_errors()
    {
        using var iso = new IsolatedKey();

        Assert.False(_registry.KeyExists(iso.Ref));
        Assert.Empty(_registry.GetSubKeyNames(iso.Ref));
        Assert.Empty(_registry.GetValueNames(iso.Ref));
        Assert.Null(_registry.GetValue(iso.Ref, "x"));
    }

    [Fact]
    public void Delete_value_and_leaf_key_work_but_a_key_with_subkeys_is_never_deleted()
    {
        using var iso = new IsolatedKey();
        var child = iso.Ref.Child("child");
        _registry.CreateKey(child);
        _registry.SetValue(iso.Ref, "v", RegistryData.String("1"));

        _registry.DeleteValue(iso.Ref, "v");
        Assert.Null(_registry.GetValue(iso.Ref, "v"));

        Assert.ThrowsAny<Exception>(() => _registry.DeleteKey(iso.Ref));
        Assert.True(_registry.KeyExists(child));

        _registry.DeleteKey(child);
        Assert.False(_registry.KeyExists(child));
    }

    [Fact]
    public void Top_level_keys_cannot_be_deleted()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _registry.DeleteKey(new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, "Software")));
    }
}

public sealed class RegistryRepairOnRealRegistryTests
{
    [Fact]
    public async Task Scan_backup_repair_and_restore_work_against_a_real_isolated_hkcu_key()
    {
        using var iso = new IsolatedKey();
        using var temp = new TempDirectory();
        var registry = new WindowsRegistryAccess();
        var layout = new AppDataLayout(temp.Combine("data"));
        layout.EnsureCreated();
        var probe = new FakeFileProbe().AddFixedDrive("C:");
        var locations = new RegistryScanLocations { StartupKeys = new[] { iso.Ref } };
        var scanner = new RegistryScanner(registry, probe, new WindowsEnvironmentExpander(), locations);
        var log = new RecordingLogger();
        var repair = new RegistryRepairService(registry, scanner, new RegistryBackupService(registry, layout, log),
            new FakeRestorePoint(), new FakePrivilege(), new RunGate(), log);

        registry.CreateKey(iso.Ref);
        registry.SetValue(iso.Ref, "Gone", RegistryData.String(@"C:\CleanSlateTests\definitely-missing.exe"));

        var issue = Assert.Single(scanner.Scan(CancellationToken.None).Issues);
        var report = await repair.RepairAsync(new[] { issue }, false, null, CancellationToken.None);

        Assert.Equal(1, report.Repaired);
        Assert.Null(registry.GetValue(iso.Ref, "Gone"));

        var backup = Assert.Single(repair.ListBackups());
        var restore = repair.Restore(backup.BackupId);
        Assert.True(restore.Success, restore.Message);
        Assert.Equal(@"C:\CleanSlateTests\definitely-missing.exe", registry.GetValue(iso.Ref, "Gone")!.AsText);
    }
}

public sealed class WindowsPlatformTests
{
    [Fact]
    public void System_paths_are_absolute_and_the_windows_directory_exists()
    {
        var paths = WindowsSystemPaths.Create();

        Assert.True(Path.IsPathRooted(paths.UserProfile));
        Assert.True(Path.IsPathRooted(paths.UserTemp));
        Assert.True(Directory.Exists(paths.WindowsDirectory));
        Assert.True(Path.IsPathRooted(paths.Downloads));
        Assert.Equal(Path.Combine(paths.WindowsDirectory, "Temp"), paths.WindowsTemp);
    }

    [Fact]
    public void Protected_paths_built_from_real_locations_protect_documents_and_the_windows_directory()
    {
        var paths = WindowsSystemPaths.Create();
        var protectedPaths = new ProtectedPaths(paths);

        Assert.True(protectedPaths.IsInsideProtectedSubtree(Path.Combine(paths.Documents, "a.docx"), out _));
        Assert.True(protectedPaths.IsInsideProtectedSubtree(Path.Combine(paths.WindowsDirectory, "System32", "kernel32.dll"), out _));
    }

    [Fact]
    public void Real_user_temp_is_an_approved_cleaning_root_but_the_user_profile_is_not()
    {
        var paths = WindowsSystemPaths.Create();
        var protectedPaths = new ProtectedPaths(paths);

        Assert.False(protectedPaths.IsInsideProtectedSubtree(Path.Combine(paths.UserTemp, "some-file.tmp"), out _));
        Assert.True(protectedPaths.IsForbiddenRoot(paths.UserProfile, out _));
    }

    [Fact]
    public void Recycle_bin_can_be_queried_without_error()
    {
        var info = new WindowsRecycleBinService().Query();

        Assert.True(info.Bytes >= 0);
        Assert.True(info.Items >= 0);
    }

    [Fact]
    public void Elevation_detection_does_not_throw()
    {
        _ = new ElevationService().IsElevated;
    }

    [Fact]
    public void Fixed_drive_probe_recognizes_the_system_drive_and_rejects_unc_paths()
    {
        var probe = new WindowsFileProbe();

        Assert.True(probe.IsOnFixedLocalDrive(Environment.SystemDirectory));
        Assert.False(probe.IsOnFixedLocalDrive(@"\\server\share\file.exe"));
    }

    [Fact]
    public async Task Process_runner_refuses_anything_that_is_not_an_approved_windows_tool()
    {
        var runner = new ProcessRunner(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c echo hi", TimeSpan.FromSeconds(5), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(@"C:\Temp\dism.exe", "", TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    [Fact]
    public async Task Process_runner_runs_an_approved_tool_and_reports_its_exit_code()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var runner = new ProcessRunner(windows);

        var result = await runner.RunAsync(powershell, "-NoProfile -NonInteractive -Command \"exit 3\"", TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Environment_expander_expands_known_variables_and_leaves_unknown_ones()
    {
        var env = new WindowsEnvironmentExpander();

        Assert.DoesNotContain("%SystemRoot%", env.Expand(@"%SystemRoot%\x"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"%CleanSlateUndefinedVar%\x", env.Expand(@"%CleanSlateUndefinedVar%\x"));
    }
}
