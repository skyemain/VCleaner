using CleanSlate.Cleaners.Registry;
using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.Presentation.Infrastructure;
using CleanSlate.Presentation.ViewModels;
using CleanSlate.TestSupport;

namespace CleanSlate.IntegrationTests;

public sealed class FakeDialogs : IDialogService
{
    public bool Answer { get; set; } = true;
    public List<(string Title, string Message)> Confirmations { get; } = new();
    public List<(string Title, string Message)> Messages { get; } = new();
    public string? SaveFile { get; set; }
    public string? Folder { get; set; }
    public string? File { get; set; }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Cancel")
    {
        Confirmations.Add((title, message));
        return Task.FromResult(Answer);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        Messages.Add((title, message));
        return Task.CompletedTask;
    }

    public string? PickSaveFile(string suggestedName, string filter) => SaveFile;

    public string? PickFolder(string title) => Folder;

    public string? PickFile(string title) => File;

    public void OpenFolder(string path)
    {
    }
}

public sealed class FakeNotifications : INotificationService
{
    public List<(string Title, string Message)> Sent { get; } = new();

    public void NotifyCompleted(string title, string message) => Sent.Add((title, message));
}

public sealed class FakeElevation : IElevationService
{
    public bool IsElevated { get; set; }
    public ElevationRestartResult Result { get; set; } = ElevationRestartResult.Started;
    public int RestartCalls { get; private set; }

    public ElevationRestartResult RestartElevated()
    {
        RestartCalls++;
        return Result;
    }
}

public sealed class FakeLifecycle : IAppLifecycle
{
    public string? ExecutablePath { get; set; } = @"C:\Apps\CleanSlate\CleanSlate.exe";
    public int ShutdownCalls { get; private set; }

    public void Shutdown() => ShutdownCalls++;
}

public sealed class FakeLogs : ILogAccess
{
    public string LogsDirectory { get; set; } = "logs";
    public int ClearCalls { get; private set; }

    public void ClearLogs() => ClearCalls++;
}

public sealed class FakeStartup : IStartupRegistration
{
    public bool IsRegistered { get; private set; }
    public string? Path { get; private set; }
    public bool Fail { get; set; }

    public void Register(string executablePath)
    {
        if (Fail)
        {
            throw new UnauthorizedAccessException("blocked");
        }

        IsRegistered = true;
        Path = executablePath;
    }

    public void Unregister() => IsRegistered = false;
}

public sealed class MemorySettingsStore : ISettingsStore
{
    public CleanSlateSettings Stored { get; private set; } = new();
    public int Saves { get; private set; }

    public CleanSlateSettings Load() => Stored.Clone();

    public void Save(CleanSlateSettings settings)
    {
        Saves++;
        Stored = settings.Clone();
    }

    public void Reset() => Stored = new CleanSlateSettings();
}

/// <summary>Wires the real engine, real modules and the real view models to fakes for the Windows-only edges.</summary>
public sealed class PresentationHarness : IDisposable
{
    public PresentationHarness(Action<CleanSlateSettings>? configure = null, ICleaningEngine? engineOverride = null, RegistryScanLocations? registryLocations = null)
    {
        Core = new Harness(registryLocations);
        Store = new MemorySettingsStore();
        var initial = Harness.Minimal(configure);
        Store.Save(initial);
        Settings = new SettingsService(Store);
        History = new HistoryStore(Core.AppData, Core.Log);
        Dialogs = new FakeDialogs();
        Notifications = new FakeNotifications();
        Elevation = new FakeElevation();
        Lifecycle = new FakeLifecycle();
        Logs = new FakeLogs();
        Startup = new FakeStartup();

        Context = new PresentationContext
        {
            Engine = engineOverride ?? Core.Engine,
            RegistryRepair = Core.Repair,
            Settings = Settings,
            History = History,
            Dispatcher = new InlineDispatcher(),
            Dialogs = Dialogs,
            Notifications = Notifications,
            Elevation = Elevation,
            Lifecycle = Lifecycle,
            Logs = Logs,
            Startup = Startup,
            Log = Core.Log
        };
        Main = new MainViewModel(Context);
    }

    public Harness Core { get; }
    public MemorySettingsStore Store { get; }
    public SettingsService Settings { get; }
    public HistoryStore History { get; }
    public FakeDialogs Dialogs { get; }
    public FakeNotifications Notifications { get; }
    public FakeElevation Elevation { get; }
    public FakeLifecycle Lifecycle { get; }
    public FakeLogs Logs { get; }
    public FakeStartup Startup { get; }
    public PresentationContext Context { get; }
    public MainViewModel Main { get; }

    public string UserTempFile(string name, int size, DateTime? writeUtc = null) =>
        Core.Temp.CreateFile("Users/tester/AppData/Local/Temp/" + name, size, writeUtc ?? Harness.OldUtc);

    public static async Task<T> WaitFor<T>(Func<T?> probe, int timeoutMs = 10_000) where T : class
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var value = probe();
            if (value is not null)
            {
                return value;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not met in time.");
    }

    public void Dispose() => Core.Dispose();
}

/// <summary>An engine that blocks until canceled, for cancellation and error-state tests.</summary>
public sealed class ControllableEngine : ICleaningEngine
{
    public Func<CleanSlateSettings, CancellationToken, Task<ScanReport>>? OnScan { get; set; }
    public Func<CleanPlan, CancellationToken, Task<CleanReport>>? OnClean { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsBusy { get; private set; }

    public async Task<ScanReport> ScanAsync(CleanSlateSettings settings, IProgress<EngineProgress>? progress, CancellationToken cancellationToken)
    {
        IsBusy = true;
        Started.TrySetResult();
        try
        {
            return await OnScan!(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<CleanReport> CleanAsync(CleanPlan plan, CleanSlateSettings settings, IProgress<EngineProgress>? progress, CancellationToken cancellationToken) =>
        OnClean!(plan, cancellationToken);
}

public static class TestRegistry
{
    public static readonly RegistryKeyRef RunKey = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\CleanSlateTest\Run");

    public static RegistryScanLocations Locations { get; } = new() { StartupKeys = new[] { RunKey } };
}
