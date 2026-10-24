using CleanSlate.Cleaners.Registry;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Services;

namespace CleanSlate.Presentation.Infrastructure;

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
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public string VersionText { get; init; } = "1.0.0";
}
