using System.Collections.ObjectModel;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.Presentation.ViewModels;

public enum AppState
{
    Ready,
    Scanning,
    AwaitingReview,
    Cleaning,
    Completed
}

/// <summary>
/// Drives the single main window: Ready -> Scanning -> (AwaitingReview) -> Cleaning -> Completed.
/// One-click mode never skips the review screen when the scan contains an operation that needs confirmation.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly PresentationContext _ctx;
    private CancellationTokenSource? _cts;
    private int _runId;

    private AppState _state = AppState.Ready;
    private ObservableObject? _currentPage;
    private string _operationText = string.Empty;
    private double _progressFraction;
    private bool _isIndeterminate = true;
    private string _headline = string.Empty;
    private string _detail = string.Empty;
    private string _errorMessage = string.Empty;
    private string _lastCleanedText = string.Empty;
    private ScanReport? _scan;
    private CleanReport? _clean;
    private int _registryRepaired;
    private long _liveBytes;
    private long _liveItems;
    private CancellationTokenSource? _autoCts;
    private string _autoText = string.Empty;
    private string _autoCancelText = string.Empty;
    private string _ramText = string.Empty;
    private bool _ramBusy;

    public MainViewModel(PresentationContext ctx)
    {
        _ctx = ctx;
        Rows = new ObservableCollection<CategoryRowViewModel>
        {
            new(CleanerGroup.SystemJunk, "Temp, Prefetch, caches, logs and crash reports"),
            new(CleanerGroup.Chrome, "Cache for every profile; sign-ins kept"),
            new(CleanerGroup.Edge, "Cache for every profile; sign-ins kept"),
            new(CleanerGroup.Registry, "Broken entries, only fixed after your review"),
            new(CleanerGroup.Privacy, "Run history and recent items, opt-in")
        };

        PrimaryCommand = new AsyncRelayCommand(() => RunAsync(automatic: false), () => CanStart, OnUnexpectedError);
        CancelAutoCommand = new RelayCommand(CancelAuto, () => ShowAuto);
        FreeRamCommand = new AsyncRelayCommand(FreeRamAsync, () => CanFreeRam, OnUnexpectedError);
        CancelCommand = new RelayCommand(Cancel, () => State is AppState.Scanning or AppState.Cleaning);
        DoneCommand = new RelayCommand(Done, () => State == AppState.Completed);
        ViewDetailsCommand = new RelayCommand(OpenDetails, () => HasResults && CanNavigate);
        ReviewRegistryCommand = new RelayCommand(OpenRegistryReview, () => HasRegistryIssues && CanNavigate);
        OpenSettingsCommand = new RelayCommand(OpenSettings, () => CanNavigate);
        RestartAsAdministratorCommand = new AsyncRelayCommand(RestartAsAdministratorAsync, () => CanNavigate && !_ctx.Elevation.IsElevated, OnUnexpectedError);

        RefreshLastCleaned();
        RefreshRam(null);
    }

    public ObservableCollection<CategoryRowViewModel> Rows { get; }

    public RelayCommand CancelAutoCommand { get; }

    public AsyncRelayCommand FreeRamCommand { get; }

    /// <summary>"Starts in 5 s" or "Closing in 12 s" while an automatic run is waiting; empty otherwise.</summary>
    public string AutoText
    {
        get => _autoText;
        private set
        {
            if (SetProperty(ref _autoText, value))
            {
                OnPropertyChanged(nameof(ShowAuto));
                CancelAutoCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AutoCancelText
    {
        get => _autoCancelText;
        private set => SetProperty(ref _autoCancelText, value);
    }

    public bool ShowAuto => !string.IsNullOrEmpty(AutoText);

    public string RamText
    {
        get => _ramText;
        private set => SetProperty(ref _ramText, value);
    }

    public bool ShowRam => _ctx.Memory is not null;

    public bool CanFreeRam => _ctx.Memory is not null && !_ramBusy && State is AppState.Ready or AppState.Completed;

    public AsyncRelayCommand PrimaryCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand DoneCommand { get; }

    public RelayCommand ViewDetailsCommand { get; }

    public RelayCommand ReviewRegistryCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public AsyncRelayCommand RestartAsAdministratorCommand { get; }

    public AppState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(PrimaryButtonText));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(ShowProgress));
                OnPropertyChanged(nameof(ShowResults));
                OnPropertyChanged(nameof(ShowNotice));
                OnPropertyChanged(nameof(CanCancel));
                OnPropertyChanged(nameof(ShowDone));
                RaiseAllCommands();
            }
        }
    }

    public ObservableObject? CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsHome));
                RaiseAllCommands();
            }
        }
    }

    public bool IsHome => CurrentPage is null;

    public bool IsBusy => State is AppState.Scanning or AppState.Cleaning;

    public bool CanCancel => IsBusy;

    public bool ShowProgress => IsBusy;

    public bool ShowResults => State == AppState.Completed;

    public bool ShowDone => State == AppState.Completed;

    public bool CanStart => State is AppState.Ready or AppState.Completed;

    public string PrimaryButtonText => State switch
    {
        AppState.Scanning => "Scanning...",
        AppState.AwaitingReview => "Waiting for review...",
        AppState.Cleaning => "Cleaning...",
        AppState.Completed => "Clean Again",
        _ => "Clean Now"
    };

    public string OperationText
    {
        get => _operationText;
        private set => SetProperty(ref _operationText, value);
    }

    public double ProgressFraction
    {
        get => _progressFraction;
        private set => SetProperty(ref _progressFraction, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public string Headline
    {
        get => _headline;
        private set
        {
            if (SetProperty(ref _headline, value))
            {
                OnPropertyChanged(nameof(ShowNotice));
            }
        }
    }

    /// <summary>A short message (for example "Canceled") shown when there is no completed result card.</summary>
    public bool ShowNotice => !ShowResults && !string.IsNullOrEmpty(Headline);

    public string Detail
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string LastCleanedText
    {
        get => _lastCleanedText;
        private set => SetProperty(ref _lastCleanedText, value);
    }

    public string LiveStatsText => State switch
    {
        AppState.Scanning => $"{_liveItems:N0} items examined",
        AppState.Cleaning when _liveBytes > 0 => $"{ByteFormatter.Format(_liveBytes)} recovered so far",
        _ => string.Empty
    };

    public string ShortVersion
    {
        get
        {
            var text = _ctx.VersionText;
            var plus = text.IndexOf('+', StringComparison.Ordinal);
            return "v" + (plus > 0 ? text[..plus] : text);
        }
    }

    public bool IsElevated => _ctx.Elevation.IsElevated;

    public bool ShowAdminHint => !_ctx.Elevation.IsElevated && IsHome;

    public string AdminHint => "Standard rights: Windows Temp, Prefetch and Windows logs need administrator access and are skipped.";

    public bool HasResults => _scan is not null && CanNavigate;

    public bool HasRegistryIssues => _scan is not null && CanNavigate && RegistryIssueCount > 0;

    public int RegistryIssueCount =>
        _scan is null ? 0 : Math.Max(0, _scan.AllFindings.Count(f => f.Action == ActionType.RepairRegistryEntry) - _registryRepaired);

    public string ReviewRegistryText => $"Review {RegistryIssueCount} registry fix{(RegistryIssueCount == 1 ? string.Empty : "es")}";

    private bool CanNavigate => State is AppState.Ready or AppState.Completed;

    public ScanReport? LastScan => _scan;

    public CleanReport? LastClean => _clean;

    private void RaiseAllCommands()
    {
        PrimaryCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        DoneCommand.RaiseCanExecuteChanged();
        ViewDetailsCommand.RaiseCanExecuteChanged();
        ReviewRegistryCommand.RaiseCanExecuteChanged();
        OpenSettingsCommand.RaiseCanExecuteChanged();
        RestartAsAdministratorCommand.RaiseCanExecuteChanged();
        FreeRamCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(ShowAdminHint));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasRegistryIssues));
    }

    private void RefreshLastCleaned()
    {
        try
        {
            var last = _ctx.History.GetLastCleanup();
            LastCleanedText = last?.CleanupTimestamp is { } when
                ? $"Last cleaned {when.LocalDateTime:MMM d, yyyy 'at' h:mm tt} · {ByteFormatter.Format(last.TotalRecoveredBytes)} recovered"
                : "Never cleaned yet";
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Could not read history", ex);
            LastCleanedText = string.Empty;
        }
    }

    private void Cancel()
    {
        if (State is AppState.Scanning or AppState.Cleaning)
        {
            OperationText = "Canceling...";
            _cts?.Cancel();
        }
    }

    private void Done()
    {
        CancelAuto();
        ErrorMessage = string.Empty;
        Headline = string.Empty;
        Detail = string.Empty;
        foreach (var row in Rows)
        {
            row.Set(GroupStatusFormatter.Describe(row.Group, _scan, _clean, _registryRepaired));
        }

        State = AppState.Ready;
        RefreshLastCleaned();
    }

    /// <summary>
    /// The sign-in flow: wait a few seconds (cancelable), scan, clean only what is safe to run unattended, show the result, then close.
    /// Nothing is ever asked, so anything that needs confirmation (Recycle Bin, privacy, website storage, registry) is skipped.
    /// </summary>
    public async Task RunAutomaticAsync()
    {
        if (!CanStart)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _autoCts = cts;
        try
        {
            if (!await CountdownAsync("Automatic clean starts in", "Cancel", _ctx.AutoStartDelay, cts.Token).ConfigureAwait(true))
            {
                return;
            }

            await RunAsync(automatic: true).ConfigureAwait(true);

            if (State == AppState.Completed && _ctx.Settings.Current.FreeRamAfterAutoClean && !cts.IsCancellationRequested)
            {
                await FreeRamAsync().ConfigureAwait(true);
            }

            if (State == AppState.Completed && _ctx.Settings.Current.CloseAfterAutoClean && !cts.IsCancellationRequested)
            {
                if (await CountdownAsync("Closing in", "Keep open", _ctx.AutoCloseDelay, cts.Token).ConfigureAwait(true))
                {
                    _ctx.Lifecycle.Shutdown();
                }
            }
        }
        finally
        {
            AutoText = string.Empty;
            if (ReferenceEquals(_autoCts, cts))
            {
                _autoCts = null;
            }

            cts.Dispose();
        }
    }

    /// <summary>Counts down in whole seconds. Returns false when the user canceled.</summary>
    private async Task<bool> CountdownAsync(string label, string cancelText, TimeSpan duration, CancellationToken token)
    {
        AutoCancelText = cancelText;
        var seconds = (int)Math.Ceiling(duration.TotalSeconds);
        try
        {
            for (var remaining = seconds; remaining > 0; remaining--)
            {
                AutoText = $"{label} {remaining} s";
                await Task.Delay(TimeSpan.FromSeconds(1), _ctx.Time, token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        AutoText = string.Empty;
        return !token.IsCancellationRequested;
    }

    private void CancelAuto() => _autoCts?.Cancel();

    private async Task FreeRamAsync()
    {
        var memory = _ctx.Memory;
        if (memory is null || _ramBusy)
        {
            return;
        }

        _ramBusy = true;
        FreeRamCommand.RaiseCanExecuteChanged();
        RamText = "Freeing RAM...";
        try
        {
            var result = await Task.Run(() => memory.Clean(CancellationToken.None)).ConfigureAwait(true);
            RefreshRam(result);
        }
        finally
        {
            _ramBusy = false;
            FreeRamCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshRam(MemoryCleanResult? result)
    {
        if (_ctx.Memory is null)
        {
            RamText = string.Empty;
            return;
        }

        try
        {
            var status = result?.After ?? _ctx.Memory.GetStatus();
            var usage = $"{ByteFormatter.Format(status.UsedBytes)} in use of {ByteFormatter.Format(status.TotalBytes)}";
            RamText = result is null
                ? usage
                : result.FreedBytes > 0
                    ? $"Freed {ByteFormatter.Format(result.FreedBytes)} · {usage}"
                    : $"Already lean, nothing more to free · {usage}";
        }
        catch (Exception ex)
        {
            _ctx.Log.Warning("Could not read memory status: " + ex.Message);
            RamText = string.Empty;
        }
    }

    private async Task RunAsync(bool automatic)
    {
        if (!CanStart)
        {
            return;
        }

        if (!automatic)
        {
            CancelAuto();
        }

        var runId = ++_runId;
        _scan = null;
        _clean = null;
        _registryRepaired = 0;
        ErrorMessage = string.Empty;
        Headline = string.Empty;
        Detail = string.Empty;
        _liveBytes = 0;
        _liveItems = 0;
        foreach (var row in Rows)
        {
            row.Set(("Waiting...", RowKind.Neutral));
        }

        var settings = _ctx.Settings.Current.Clone();
        if (automatic)
        {
            settings.ReviewBeforeCleaning = false;
            settings.AskBeforeClosingBrowsers = false;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            State = AppState.Scanning;
            OperationText = "Scanning...";
            IsIndeterminate = true;
            ProgressFraction = 0;

            await ConfirmBrowserCloseAsync(settings).ConfigureAwait(true);

            var scanProgress = new DispatchedProgress<EngineProgress>(_ctx.Dispatcher, p => ApplyProgress(runId, p));
            var scan = await Task.Run(() => _ctx.Engine.ScanAsync(settings, scanProgress, token), CancellationToken.None).ConfigureAwait(true);
            _scan = scan;

            if (scan.WasCanceled || token.IsCancellationRequested)
            {
                FinishCanceled("Scan canceled. Nothing was changed.");
                return;
            }

            ShowScanRows();

            var mode = PlanMode.Automatic;
            var forced = PlanBuilder.RequiresReview(scan);
            if (!automatic && (settings.ReviewBeforeCleaning || forced))
            {
                mode = PlanMode.Reviewed;
                if (!await ReviewAsync(scan, forced && !settings.ReviewBeforeCleaning).ConfigureAwait(true))
                {
                    FinishCanceled("Cleaning canceled. Nothing was changed.");
                    return;
                }
            }

            var plan = PlanBuilder.Build(scan, mode);
            if (plan.ActionCount == 0)
            {
                CompleteWithoutCleaning(scan, mode, automatic && forced);
                return;
            }

            State = AppState.Cleaning;
            OperationText = "Cleaning...";
            IsIndeterminate = true;
            ProgressFraction = 0;
            var cleanProgress = new DispatchedProgress<EngineProgress>(_ctx.Dispatcher, p => ApplyProgress(runId, p));
            var clean = await Task.Run(() => _ctx.Engine.CleanAsync(plan, settings, cleanProgress, token), CancellationToken.None).ConfigureAwait(true);
            _clean = clean;
            Complete(scan, clean, mode, automatic && forced);
        }
        catch (EngineBusyException ex)
        {
            ErrorMessage = ex.Message;
            ResetRowsAfterFailure();
            State = AppState.Ready;
        }
        catch (OperationCanceledException)
        {
            FinishCanceled("Canceled.");
        }
        catch (Exception ex)
        {
            OnUnexpectedError(ex);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Running browsers can only have their cache cleaned after they are closed. Ask once; if the user declines, the clean still runs
    /// but leaves the running browsers alone.
    /// </summary>
    private async Task ConfirmBrowserCloseAsync(CleanSlate.Core.Configuration.CleanSlateSettings settings)
    {
        var running = _ctx.Browsers?.GetBrowsersThatWouldBeClosed(settings) ?? Array.Empty<string>();
        if (running.Count == 0 || !settings.AskBeforeClosingBrowsers)
        {
            return;
        }

        var names = string.Join(" and ", running);
        var plural = running.Count > 1;
        var ok = await _ctx.Dialogs.ConfirmAsync(
            $"Close {names} to clean {(plural ? "them" : "it")}?",
            $"{names} {(plural ? "are" : "is")} running, and a browser's cache cannot be cleaned while it is open.\n\n" +
            $"CleanSlate will close {(plural ? "them" : "it")} politely first and only end what is still running after a few seconds. " +
            "Your signed-in accounts, saved passwords, cookies, bookmarks and settings are not touched, so you stay signed in. " +
            "Unsaved text in open pages may be lost.",
            $"Close {(plural ? "browsers" : names)} and clean", "Skip browsers").ConfigureAwait(true);
        if (!ok)
        {
            settings.CloseBrowsersBeforeCleaning = false;
        }
    }

    private async Task<bool> ReviewAsync(ScanReport scan, bool forced)
    {
        State = AppState.AwaitingReview;
        var review = new ReviewViewModel(scan, forced, _ => { });
        CurrentPage = review;
        var confirmed = await review.Completion.ConfigureAwait(true);
        CurrentPage = null;
        return confirmed;
    }

    private void ShowScanRows()
    {
        foreach (var row in Rows)
        {
            row.Set(GroupStatusFormatter.Describe(row.Group, _scan, null, _registryRepaired));
        }
    }

    private void ApplyProgress(int runId, EngineProgress progress)
    {
        var expected = progress.Phase switch
        {
            EnginePhase.Scanning => AppState.Scanning,
            EnginePhase.Cleaning => AppState.Cleaning,
            _ => (AppState?)null
        };
        if (runId != _runId || expected is null || State != expected)
        {
            return;
        }

        foreach (var row in Rows)
        {
            if (progress.Groups.TryGetValue(row.Group, out var group))
            {
                row.Set(GroupStatusFormatter.FromProgress(row.Group, group));
            }
        }

        if (!string.IsNullOrEmpty(progress.Operation) && _cts is { IsCancellationRequested: false })
        {
            OperationText = progress.Operation;
        }

        IsIndeterminate = !progress.IsDeterminate;
        ProgressFraction = progress.Fraction;
        _liveItems = progress.ItemsScanned;
        _liveBytes = progress.BytesRecovered;
        OnPropertyChanged(nameof(LiveStatsText));
    }

    private void FinishCanceled(string message)
    {
        ResetRowsAfterFailure();
        if (_scan is not null && !(_scan.WasCanceled))
        {
            ShowScanRows();
        }

        Headline = "Canceled";
        Detail = message;
        State = AppState.Ready;
        CurrentPage = null;
    }

    private void ResetRowsAfterFailure()
    {
        if (_scan is null)
        {
            foreach (var row in Rows)
            {
                row.Set(("Ready to scan", RowKind.Neutral));
            }
        }
    }

    private const string SkippedForReviewNote = " Items that need your confirmation were skipped; run CleanSlate yourself to review them.";

    private void CompleteWithoutCleaning(ScanReport scan, PlanMode mode, bool skippedForReview = false)
    {
        SaveSession(SessionRecorder.Create(scan, null, mode, _registryRepaired));
        Headline = "Nothing to clean";
        Detail = "Your system is already tidy, or the remaining items need review or administrator rights." + (skippedForReview ? SkippedForReviewNote : string.Empty);
        ShowScanRows();
        State = AppState.Completed;
        NotifyIfAppropriate("Nothing to clean", Detail);
    }

    private void Complete(ScanReport scan, CleanReport clean, PlanMode mode, bool skippedForReview = false)
    {
        SaveSession(SessionRecorder.Create(scan, clean, mode, _registryRepaired));

        foreach (var row in Rows)
        {
            row.Set(GroupStatusFormatter.Describe(row.Group, scan, clean, _registryRepaired));
        }

        Headline = clean.WasCanceled
            ? $"Canceled · recovered {ByteFormatter.Format(clean.BytesRecovered)} before stopping"
            : clean.BytesRecovered > 0 ? $"Recovered {ByteFormatter.Format(clean.BytesRecovered)}" : "Cleanup finished";
        Detail = $"{clean.ItemsCleaned:N0} items removed · {clean.ItemsSkipped:N0} skipped · {clean.Errors:N0} error{(clean.Errors == 1 ? string.Empty : "s")} · {clean.Duration.TotalSeconds:0.0} s";
        if (clean.Errors > 0)
        {
            Detail += ". Open details to see what could not be cleaned.";
        }

        if (skippedForReview)
        {
            Detail += "." + SkippedForReviewNote;
        }

        State = AppState.Completed;
        NotifyIfAppropriate("CleanSlate finished", $"{Headline}. {Detail}");
    }

    private void NotifyIfAppropriate(string title, string message)
    {
        if (!_ctx.Settings.Current.ShowCompletionNotifications)
        {
            return;
        }

        try
        {
            _ctx.Notifications.NotifyCompleted(title, message);
        }
        catch (Exception ex)
        {
            _ctx.Log.Warning("Notification failed: " + ex.Message);
        }
    }

    private void SaveSession(SessionRecord record)
    {
        try
        {
            _ctx.History.Save(record);
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Could not save session history", ex);
        }
    }

    private void OnUnexpectedError(Exception ex)
    {
        _ctx.Log.Error("Unexpected error during a run", ex);
        ErrorMessage = "Something went wrong: " + ex.Message + " Nothing further was changed. Details are in the log.";
        ResetRowsAfterFailure();
        CurrentPage = null;
        State = AppState.Ready;
    }

    private void OpenSettings()
    {
        CurrentPage = new SettingsViewModel(_ctx, CloseSettings, OpenRegistryBackups, () => IsBusy);
    }

    private void CloseSettings()
    {
        CurrentPage = null;
        RefreshLastCleaned();
    }

    private void OpenDetails()
    {
        if (_scan is not null)
        {
            CurrentPage = new DetailsViewModel(_scan, _clean, () => CurrentPage = null);
        }
    }

    private void OpenRegistryReview()
    {
        if (_scan is null)
        {
            return;
        }

        var issues = _scan.AllFindings
            .Where(f => f.Action == ActionType.RepairRegistryEntry && f.Payload is RegistryIssue)
            .Select(f => (RegistryIssue)f.Payload!)
            .ToList();
        CurrentPage = new RegistryReviewViewModel(_ctx, issues, () => CurrentPage = null, OnRegistryRepaired);
    }

    private void OpenRegistryBackups()
    {
        CurrentPage = new RegistryReviewViewModel(_ctx, Array.Empty<RegistryIssue>(), () => CurrentPage = null, _ => { });
    }

    private void OnRegistryRepaired(int count)
    {
        _registryRepaired += count;
        foreach (var row in Rows.Where(r => r.Group == CleanerGroup.Registry))
        {
            row.Set(GroupStatusFormatter.Describe(row.Group, _scan, _clean, _registryRepaired));
        }

        OnPropertyChanged(nameof(RegistryIssueCount));
        OnPropertyChanged(nameof(ReviewRegistryText));
        OnPropertyChanged(nameof(HasRegistryIssues));
        ReviewRegistryCommand.RaiseCanExecuteChanged();
    }

    private async Task RestartAsAdministratorAsync()
    {
        var ok = await _ctx.Dialogs.ConfirmAsync(
            "Restart as administrator?",
            "CleanSlate will close and start again with administrator rights. Windows will ask for your permission first. If you decline, CleanSlate keeps running as it is.",
            "Restart").ConfigureAwait(true);
        if (!ok)
        {
            return;
        }

        switch (_ctx.Elevation.RestartElevated())
        {
            case ElevationRestartResult.Started:
                _ctx.Lifecycle.Shutdown();
                break;
            case ElevationRestartResult.Declined:
                await _ctx.Dialogs.ShowMessageAsync("Administrator access was not granted", "CleanSlate is still running without administrator rights.").ConfigureAwait(true);
                break;
            default:
                await _ctx.Dialogs.ShowMessageAsync("Could not restart", "CleanSlate could not start an elevated copy of itself. It is still running normally.").ConfigureAwait(true);
                break;
        }
    }
}
