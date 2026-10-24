using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CleanSlate.App.Services;
using CleanSlate.Cleaners;
using CleanSlate.Cleaners.Browsers;
using CleanSlate.Cleaners.Registry;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Install;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Services;
using CleanSlate.Presentation.Infrastructure;
using CleanSlate.Presentation.ViewModels;
using CleanSlate.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace CleanSlate.App;

public partial class App : Application
{
    private const string MutexName = @"Local\CleanSlate.App.SingleInstance";
    private const string ActivateEventName = @"Local\CleanSlate.App.Activate";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private ServiceProvider? _services;
    private FileAppLogger? _logger;
    private MainWindow? _window;
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var restarted = e.Args.Contains("--restarted", StringComparer.OrdinalIgnoreCase);
        var startup = e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase);

        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            RunUninstall(e.Args.Contains("--quiet", StringComparer.OrdinalIgnoreCase));
            Shutdown();
            return;
        }

        var skipInstallOffer = startup || restarted || e.Args.Contains("--no-install-prompt", StringComparer.OrdinalIgnoreCase);
        if (!skipInstallOffer && OfferInstall())
        {
            Shutdown();
            return;
        }

        if (!AcquireSingleInstance(restarted))
        {
            Shutdown();
            return;
        }

        SessionEnding += (_, _) => _window?.MarkSessionEnding();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _logger?.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            _services = BuildServices();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("CleanSlate could not prepare its data folder under %LOCALAPPDATA%:\n\n" + ex.Message,
                "CleanSlate", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var settings = _services.GetRequiredService<SettingsService>();
        ThemeManager.Start(settings.Current.FollowSystemTheme);

        var viewModel = _services.GetRequiredService<MainViewModel>();
        _window = new MainWindow(viewModel, () => settings.Current.MinimizeToTray);
        MainWindow = _window;
        _tray = (TrayService)_services.GetRequiredService<INotificationService>();

        settings.Changed += (_, _) =>
        {
            ThemeManager.SetFollowSystem(settings.Current.FollowSystemTheme);
            _tray.Refresh();
        };
        _tray.Refresh();

        _logger?.Info($"CleanSlate {VersionText()} started (elevated: {_services.GetRequiredService<IPrivilegeInfo>().IsElevated}).");

        HealStartupEntries(settings);

        var autoClean = startup && settings.Current.AutoCleanOnStartup;
        if (startup && settings.Current.MinimizeToTray && !autoClean)
        {
            _window.WindowState = WindowState.Minimized;
            _window.ShowActivated = false;
            _window.Show();
            _window.Hide();
        }
        else
        {
            _window.Show();
        }

        if (autoClean)
        {
            _window.BringToFront();
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await viewModel.RunAutomaticAsync();
                }
                catch (Exception ex)
                {
                    _logger?.Error("The automatic clean failed", ex);
                }
            }), DispatcherPriority.Background);
        }

        StartActivationListener();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _logger?.Info("CleanSlate exited.");
        _logger?.Dispose();
        _activateEvent?.Dispose();
        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned by this thread (startup was aborted early).
            }

            _instanceMutex.Dispose();
        }

        _services?.Dispose();
        base.OnExit(e);
    }

    private static InstallService CreateInstallService(AppDataLayout layout) =>
        new(new WindowsInstallPlatform(layout), new WindowsRegistryAccess(), new StartupRegistration(), NullAppLogger.Instance, new WindowsStartupTask());

    /// <summary>
    /// Re-creates the sign-in startup entries at launch when the settings say they should exist. This heals an entry that was
    /// lost or left pointing at a removed copy, which otherwise silently breaks "clean automatically when I sign in".
    /// Only the installed copy does this, so a portable run never registers itself.
    /// </summary>
    private void HealStartupEntries(SettingsService settings)
    {
        var current = settings.Current;
        var path = Environment.ProcessPath;
        if (!current.LaunchOnStartup || string.IsNullOrEmpty(path) || _services is null ||
            !_services.GetRequiredService<InstallService>().IsRunningFromInstallFolder(path))
        {
            return;
        }

        if (!current.ElevatedStartup)
        {
            try
            {
                _services.GetRequiredService<IStartupRegistration>().Register(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger?.Warning("The sign-in startup entry could not be refreshed: " + ex.Message);
            }
        }
        else if (_services.GetRequiredService<IPrivilegeInfo>().IsElevated)
        {
            try
            {
                var task = _services.GetRequiredService<IStartupTask>();
                if (!task.IsRegistered)
                {
                    task.Register(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                _logger?.Warning("The administrator sign-in task could not be refreshed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Offers to install this copy (replacing an older installed version) before the app starts. Returns true when the installed copy
    /// was started instead and this process should exit. Nothing is asked when a <c>portable.txt</c> file sits next to the exe.
    /// </summary>
    private bool OfferInstall()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            if (File.Exists(Path.Combine(folder, "portable.txt")))
            {
                return false;
            }

            var service = CreateInstallService(AppDataLayout.ForCurrentUser());
            if (service.IsRunningFromInstallFolder(path))
            {
                return false;
            }

            var current = VersionText();
            var installed = service.GetInstalled();
            var certificate = new WindowsCertificateTrust(new ElevationService(), Assembly.GetExecutingAssembly(), path);
            var manager = new InstallManager(service, current, certificate);

            if (installed is not null && string.Equals(installed.Version, current, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    InstallManager.Launch(installed.ExecutablePath);
                    return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                {
                    // The installed copy would not start (for example blocked by policy); run this copy instead.
                    return false;
                }
            }

            var message = installed is null
                ? $"Install CleanSlate {current}?\n\nIt is copied to your user profile and added to the Start menu. No administrator rights are needed, and you can uninstall it from Windows Settings.{TrustLine(certificate)}\n\nYes installs and opens it.\nNo runs this copy once without installing."
                : $"CleanSlate {installed.Version} is installed. Replace it with {current}?\n\nThe old version is closed and removed. Your settings and history are kept.{TrustLine(certificate)}\n\nYes replaces it and opens the new version.\nNo runs this copy once without installing.";
            var choice = MessageBox.Show(message, "CleanSlate setup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                return false;
            }

            var result = manager.InstallThisCopy(trustCertificate: true);
            if (result.Success && result.ShouldExit)
            {
                return true;
            }

            MessageBox.Show(result.Message + (result.Success ? string.Empty : "\n\nCleanSlate will run from its current location instead."),
                "CleanSlate setup", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string TrustLine(WindowsCertificateTrust certificate) =>
        certificate.IsAvailable && !certificate.IsTrusted
            ? "\n\nThis build is signed with its own certificate. Installing also trusts that certificate on this PC; Windows will ask you to confirm."
            : string.Empty;

    private void RunUninstall(bool quiet)
    {
        var layout = AppDataLayout.ForCurrentUser();
        var manager = new InstallManager(CreateInstallService(layout), VersionText(),
            new WindowsCertificateTrust(new ElevationService(), Assembly.GetExecutingAssembly(), Environment.ProcessPath));
        var removeData = false;
        if (!quiet)
        {
            var confirm = MessageBox.Show("Uninstall CleanSlate?\n\nThe program, its Start menu entry and its startup entry are removed.",
                "CleanSlate", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            removeData = MessageBox.Show(
                "Also delete CleanSlate's settings, history and registry backups, and remove its trusted signing certificate?\n\nChoose No to keep them, for example if you plan to reinstall.",
                "CleanSlate", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        var result = manager.Uninstall(removeData);
        if (!quiet)
        {
            MessageBox.Show(result.Success ? "CleanSlate was uninstalled." : result.Message, "CleanSlate",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private bool AcquireSingleInstance(bool waitForPrevious)
    {
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out var created);
            if (created)
            {
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
                return true;
            }

            if (waitForPrevious)
            {
                // After "Restart as administrator" the previous instance is closing; wait for it to let go.
                if (_instanceMutex.WaitOne(TimeSpan.FromSeconds(15)))
                {
                    _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
                    return true;
                }
            }

            using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
            existing.Set();
            return false;
        }
        catch (AbandonedMutexException)
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Another instance runs at a different privilege level and cannot be signaled.
            return false;
        }
    }

    private void StartActivationListener()
    {
        var handle = _activateEvent;
        if (handle is null)
        {
            return;
        }

        var thread = new Thread(() =>
        {
            try
            {
                while (handle.WaitOne())
                {
                    Dispatcher.BeginInvoke(new Action(() => _window?.BringToFront()));
                }
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        })
        { IsBackground = true, Name = "CleanSlate activation listener" };
        thread.Start();
    }

    private ServiceProvider BuildServices()
    {
        var layout = AppDataLayout.ForCurrentUser();
        layout.EnsureCreated();
        var paths = WindowsSystemPaths.Create();
        _logger = new FileAppLogger(layout, new LogSanitizer(paths.UserProfile));

        var services = new ServiceCollection();
        services.AddSingleton(layout);
        services.AddSingleton(paths);
        services.AddSingleton(_logger);
        services.AddSingleton<IAppLogger>(_logger);
        services.AddSingleton<ISettingsStore>(sp => new SettingsStore(layout, sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IHistoryStore>(sp => new HistoryStore(layout, sp.GetRequiredService<IAppLogger>()));

        services.AddSingleton<ElevationService>();
        services.AddSingleton<IPrivilegeInfo>(sp => sp.GetRequiredService<ElevationService>());
        services.AddSingleton<IElevationService>(sp => sp.GetRequiredService<ElevationService>());
        services.AddSingleton<IRegistryAccess, WindowsRegistryAccess>();
        services.AddSingleton<IFileProbe, WindowsFileProbe>();
        services.AddSingleton<IEnvironmentExpander, WindowsEnvironmentExpander>();
        services.AddSingleton<IRecycleBinService, WindowsRecycleBinService>();
        services.AddSingleton<IProcessRunner>(sp => new ProcessRunner(sp.GetRequiredService<CleanSlate.Core.Models.SystemPaths>().WindowsDirectory));
        services.AddSingleton<IRestorePointService>(sp => new WindowsRestorePointService(sp.GetRequiredService<IPrivilegeInfo>()));
        services.AddSingleton<IStartupRegistration, StartupRegistration>();
        services.AddSingleton<IBrowserRunningDetector, ProcessBrowserRunningDetector>();
        services.AddSingleton<IStartupTask, WindowsStartupTask>();
        services.AddSingleton<IMemoryCleaner, WindowsMemoryCleaner>();
        services.AddSingleton<IBrowserCloser, ProcessBrowserCloser>();
        services.AddSingleton<IBrowserInventory>(sp => new BrowserInventory(
            sp.GetRequiredService<CleanSlate.Core.Models.SystemPaths>(), sp.GetRequiredService<IBrowserRunningDetector>()));
        services.AddSingleton(sp => new InstallService(
            new WindowsInstallPlatform(sp.GetRequiredService<AppDataLayout>()), sp.GetRequiredService<IRegistryAccess>(),
            sp.GetRequiredService<IStartupRegistration>(), sp.GetRequiredService<IAppLogger>(), sp.GetRequiredService<IStartupTask>()));
        services.AddSingleton<ICertificateTrust>(sp => new WindowsCertificateTrust(
            sp.GetRequiredService<IPrivilegeInfo>(), Assembly.GetExecutingAssembly(), Environment.ProcessPath));
        services.AddSingleton<IInstallManager>(sp => new InstallManager(
            sp.GetRequiredService<InstallService>(), VersionText(), sp.GetRequiredService<ICertificateTrust>()));

        services.AddSingleton<RunGate>();
        services.AddSingleton(sp => new ProtectedPaths(sp.GetRequiredService<CleanSlate.Core.Models.SystemPaths>()));
        services.AddSingleton<ISafeDeletionService>(sp => new SafeDeletionService(sp.GetRequiredService<ProtectedPaths>()));
        services.AddSingleton(sp => new RegistryScanner(
            sp.GetRequiredService<IRegistryAccess>(), sp.GetRequiredService<IFileProbe>(), sp.GetRequiredService<IEnvironmentExpander>()));
        services.AddSingleton(sp => new RegistryBackupService(
            sp.GetRequiredService<IRegistryAccess>(), sp.GetRequiredService<AppDataLayout>(), sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton(sp => new RegistryRepairService(
            sp.GetRequiredService<IRegistryAccess>(), sp.GetRequiredService<RegistryScanner>(), sp.GetRequiredService<RegistryBackupService>(),
            sp.GetRequiredService<IRestorePointService>(), sp.GetRequiredService<IPrivilegeInfo>(), sp.GetRequiredService<RunGate>(),
            sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton(sp => CleanerCatalog.CreateModules(
            sp.GetRequiredService<CleanSlate.Core.Models.SystemPaths>(), sp.GetRequiredService<ISafeDeletionService>(),
            sp.GetRequiredService<IRegistryAccess>(), sp.GetRequiredService<RegistryScanner>(), sp.GetRequiredService<IFileProbe>(),
            sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<IRecycleBinService>(), sp.GetRequiredService<IBrowserRunningDetector>(),
            sp.GetRequiredService<IBrowserCloser>(), sp.GetRequiredService<IAppLogger>()));
        services.AddSingleton<ICleaningEngine>(sp => new CleaningEngine(
            sp.GetRequiredService<IReadOnlyList<ICleanerModule>>(), sp.GetRequiredService<IPrivilegeInfo>(),
            sp.GetRequiredService<IAppLogger>(), sp.GetRequiredService<RunGate>()));

        services.AddSingleton<IUiDispatcher, WpfDispatcher>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IAppLifecycle, AppLifecycle>();
        services.AddSingleton<ILogAccess, LogAccess>();
        services.AddSingleton<INotificationService>(sp => new TrayService(
            () => _window, () => sp.GetRequiredService<SettingsService>().Current.MinimizeToTray));

        services.AddSingleton(sp => new PresentationContext
        {
            Engine = sp.GetRequiredService<ICleaningEngine>(),
            RegistryRepair = sp.GetRequiredService<RegistryRepairService>(),
            Settings = sp.GetRequiredService<SettingsService>(),
            History = sp.GetRequiredService<IHistoryStore>(),
            Dispatcher = sp.GetRequiredService<IUiDispatcher>(),
            Dialogs = sp.GetRequiredService<IDialogService>(),
            Notifications = sp.GetRequiredService<INotificationService>(),
            Elevation = sp.GetRequiredService<IElevationService>(),
            Lifecycle = sp.GetRequiredService<IAppLifecycle>(),
            Logs = sp.GetRequiredService<ILogAccess>(),
            Startup = sp.GetRequiredService<IStartupRegistration>(),
            Log = sp.GetRequiredService<IAppLogger>(),
            Browsers = sp.GetRequiredService<IBrowserInventory>(),
            Installer = sp.GetRequiredService<IInstallManager>(),
            Memory = sp.GetRequiredService<IMemoryCleaner>(),
            StartupTask = sp.GetRequiredService<IStartupTask>(),
            VersionText = VersionText()
        });
        services.AddSingleton<MainViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;
        MessageBox.Show("Something unexpected went wrong. Nothing was deleted by this error.\n\n" + e.Exception.Message +
                        "\n\nDetails were written to the CleanSlate log.", "CleanSlate", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string VersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        return version;
    }
}
