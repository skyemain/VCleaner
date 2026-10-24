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

        if (startup && settings.Current.MinimizeToTray)
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
            sp.GetRequiredService<IAppLogger>()));
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
