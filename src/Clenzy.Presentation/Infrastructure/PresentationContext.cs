using Clenzy.Cleaners.Browsers;
using Clenzy.Cleaners.Registry;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Services;

namespace Clenzy.Presentation.Infrastructure;

/// <summary>Everything the view models need, supplied by the composition root (or by fakes in tests).</summary>
public sealed class PresentationContext
{
    public required ICleaningEngine Engine { get; init; }
    public required RegistryRepairService RegistryRepair { get; init; }
    public required SettingsService Settings { get; init; }
    public required IHistoryStore History { get; init; }
    public required IUiDispatcher Dispatcher { get; init; }
    public required IDialogService Dialogs { get; init; }
    public required INotificationService Notifications { get; init; }
    public required IElevationService Elevation { get; init; }
    public required IAppLifecycle Lifecycle { get; init; }
    public required ILogAccess Logs { get; init; }
    public required IStartupRegistration Startup { get; init; }
    public required IAppLogger Log { get; init; }
    public IBrowserInventory? Browsers { get; init; }
    public IInstallManager? Installer { get; init; }
    public IMemoryCleaner? Memory { get; init; }
    public IStartupTask? StartupTask { get; init; }

    /// <summary>Pause before an automatic (sign-in) clean starts, so it can be canceled.</summary>
    public TimeSpan AutoStartDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long the result stays visible after an automatic clean before the app closes itself.</summary>
    public TimeSpan AutoCloseDelay { get; init; } = TimeSpan.FromSeconds(12);
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public string VersionText { get; init; } = "1.0.0";
}
