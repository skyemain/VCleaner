using Clenzy.Core.Install;
using Clenzy.Core.Models;
using Clenzy.TestSupport;
using Xunit;

namespace Clenzy.UnitTests;

public sealed class InstallTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly DiskInstallPlatform _platform;
    private readonly InMemoryRegistry _registry = new();
    private readonly FakeStartupRegistration _startup = new();
    private readonly RecordingLogger _log = new();
    private readonly InstallService _service;

    public InstallTests()
    {
        _platform = new DiskInstallPlatform(_temp);
        _service = new InstallService(_platform, _registry, _startup, _log);
    }

    public void Dispose() => _temp.Dispose();

    private string Build(string name, string content)
    {
        var path = _temp.Combine("downloads", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static RegistryKeyRef Key => new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, InstallService.UninstallKeyPath);

    [Fact]
    public void A_fresh_install_copies_the_program_registers_it_and_adds_a_shortcut()
    {
        var source = Build("Clenzy-1.2.0-win-x64.exe", "build-1");

        var result = _service.Install(source, "1.2.0");

        var installed = Path.Combine(_platform.InstallDirectory, "Clenzy.exe");
        Assert.Equal(InstallResultKind.Installed, result.Kind);
        Assert.Null(result.PreviousVersion);
        Assert.Equal("build-1", File.ReadAllText(installed));
        Assert.Equal(installed, File.ReadAllText(_platform.StartMenuShortcutPath));
        Assert.Equal("Clenzy", _registry.GetValue(Key, "DisplayName")!.AsText);
        Assert.Equal("1.2.0", _registry.GetValue(Key, "DisplayVersion")!.AsText);
        Assert.Equal($"\"{installed}\" --uninstall", _registry.GetValue(Key, "UninstallString")!.AsText);
        Assert.Equal($"\"{installed}\" --uninstall --quiet", _registry.GetValue(Key, "QuietUninstallString")!.AsText);
        Assert.Equal(1, _registry.GetValue(Key, "NoModify")!.Value);
        var info = _service.GetInstalled();
        Assert.NotNull(info);
        Assert.Equal("1.2.0", info!.Version);
        Assert.True(_service.IsRunningFromInstallFolder(installed));
        Assert.False(_service.IsRunningFromInstallFolder(source));
    }

    [Fact]
    public void Installing_a_new_build_replaces_the_old_version_and_closes_the_running_copy()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");
        var installed = Path.Combine(_platform.InstallDirectory, "Clenzy.exe");
        File.WriteAllText(Path.Combine(_platform.InstallDirectory, "stale.txt"), "leftover");

        var result = _service.Install(Build("v2.exe", "build-2"), "1.3.0");

        Assert.Equal(InstallResultKind.Updated, result.Kind);
        Assert.Equal("1.2.0", result.PreviousVersion);
        Assert.Equal("build-2", File.ReadAllText(installed));
        Assert.Equal("1.3.0", _registry.GetValue(Key, "DisplayVersion")!.AsText);
        Assert.Contains(installed, _platform.StopRequests);
        Assert.Single(_registry.GetSubKeyNames(new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall")));
    }

    [Fact]
    public void Installing_removes_the_download_marker_from_the_installed_exe()
    {
        var source = Build("Clenzy-1.3.2-win-x64.exe", "build-1");

        _service.Install(source, "1.3.2");

        var installed = Path.Combine(_platform.InstallDirectory, "Clenzy.exe");
        Assert.Equal(new[] { installed }, _platform.RemovedDownloadMarkers);
    }

    [Fact]
    public void An_update_repoints_the_startup_entry_only_when_it_was_enabled()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");
        var installed = Path.Combine(_platform.InstallDirectory, "Clenzy.exe");

        _service.Install(Build("v2.exe", "build-2"), "1.3.0");
        Assert.False(_startup.IsRegistered);

        _startup.IsRegistered = true;
        _service.Install(Build("v3.exe", "build-3"), "1.4.0");
        Assert.True(_startup.IsRegistered);
        Assert.Equal(installed, _startup.Path);
    }

    [Fact]
    public void An_installed_copy_that_cannot_be_closed_is_left_untouched()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");
        _platform.InstancesThatCannotBeStopped = 1;

        var ex = Assert.Throws<InstallException>(() => _service.Install(Build("v2.exe", "build-2"), "1.3.0"));

        Assert.Contains("still running", ex.Message);
        Assert.Equal("build-1", File.ReadAllText(Path.Combine(_platform.InstallDirectory, "Clenzy.exe")));
        Assert.Equal("1.2.0", _registry.GetValue(Key, "DisplayVersion")!.AsText);
    }

    [Fact]
    public void Running_the_installed_copy_does_not_reinstall_over_itself()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");
        var installed = Path.Combine(_platform.InstallDirectory, "Clenzy.exe");

        var result = _service.Install(installed, "1.2.0");

        Assert.Equal(InstallResultKind.AlreadyRunningFromInstallFolder, result.Kind);
        Assert.Equal("build-1", File.ReadAllText(installed));
    }

    [Fact]
    public void A_missing_or_non_exe_source_is_rejected()
    {
        Assert.Throws<InstallException>(() => _service.Install(_temp.Combine("nope.exe"), "1.0.0"));
        Assert.Throws<InstallException>(() => _service.Install(Build("notes.txt", "x"), "1.0.0"));
        Assert.Null(_service.GetInstalled());
    }

    [Fact]
    public void Uninstall_removes_the_program_shortcut_startup_and_apps_entry_but_keeps_data_by_default()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");
        _startup.IsRegistered = true;
        Directory.CreateDirectory(_platform.UserDataDirectory);
        File.WriteAllText(Path.Combine(_platform.UserDataDirectory, "settings.json"), "{}");

        var result = _service.Uninstall(removeUserData: false);

        Assert.True(result.WasInstalled);
        Assert.False(result.UserDataRemoved);
        Assert.False(_registry.KeyExists(Key));
        Assert.False(File.Exists(_platform.StartMenuShortcutPath));
        Assert.False(_startup.IsRegistered);
        Assert.Equal(new[] { _platform.InstallDirectory }, _platform.ScheduledDeletes);
        Assert.True(File.Exists(Path.Combine(_platform.UserDataDirectory, "settings.json")));
        Assert.Null(_service.GetInstalled());
    }

    [Fact]
    public void Uninstall_can_also_remove_settings_history_and_backups()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");

        var result = _service.Uninstall(removeUserData: true);

        Assert.True(result.UserDataRemoved);
        Assert.Equal(new[] { _platform.InstallDirectory, _platform.UserDataDirectory }, _platform.ScheduledDeletes);
    }

    [Fact]
    public void Uninstall_when_nothing_is_installed_does_nothing()
    {
        var result = _service.Uninstall(removeUserData: true);

        Assert.False(result.WasInstalled);
        Assert.Empty(_platform.ScheduledDeletes);
        Assert.Equal(0, _startup.UnregisterCalls);
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("LocalAppData")]
    [InlineData("LocalAppData/Programs")]
    public void Folders_that_are_not_named_after_the_product_are_never_touched(string relative)
    {
        var victim = _temp.Combine(relative);
        Directory.CreateDirectory(victim);
        File.WriteAllText(Path.Combine(victim, "important.txt"), "keep");
        _platform.InstallDirectory = victim;
        _platform.UserDataDirectory = victim;

        Assert.Throws<InstallException>(() => _service.Install(Build("v1.exe", "build-1"), "1.2.0"));
        Assert.Throws<InstallException>(() => _service.Uninstall(removeUserData: true));

        Assert.True(File.Exists(Path.Combine(victim, "important.txt")));
        Assert.Empty(_platform.ScheduledDeletes);
    }

    [Fact]
    public void A_user_data_folder_that_is_not_named_after_the_product_is_never_scheduled_for_deletion()
    {
        _service.Install(Build("v1.exe", "build-1"), "1.2.0");
        var documents = _temp.Combine("Documents");
        Directory.CreateDirectory(documents);
        _platform.UserDataDirectory = documents;

        Assert.Throws<InstallException>(() => _service.Uninstall(removeUserData: true));

        Assert.DoesNotContain(documents, _platform.ScheduledDeletes);
        Assert.True(Directory.Exists(documents));
    }
}
