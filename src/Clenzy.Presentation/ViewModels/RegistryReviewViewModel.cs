using System.Collections.ObjectModel;
using Clenzy.Core.Models;
using Clenzy.Core.Services;
using Clenzy.Presentation.Infrastructure;

namespace Clenzy.Presentation.ViewModels;

public sealed class RegistryIssueItemViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;
    private string _status = string.Empty;
    private bool _isDone;

    public RegistryIssueItemViewModel(RegistryIssue issue, bool isElevated, Action changed)
    {
        Issue = issue;
        _changed = changed;
        NeedsAdministrator = issue.RequiresElevation && !isElevated;
        if (NeedsAdministrator)
        {
            _status = "Requires administrator";
        }
    }

    public RegistryIssue Issue { get; }

    public bool NeedsAdministrator { get; }

    public string Title => Issue.RuleTitle;

    public string Location => Issue.DisplayLocation;

    public string Reason => Issue.Reason;

    public string Evidence => Issue.Evidence;

    public string ProposedAction => Issue.ProposedAction;

    public string ConfidenceText => Issue.Confidence == IssueConfidence.High ? "High confidence" : "Medium confidence";

    public bool CanSelect => !NeedsAdministrator && !_isDone;

    public bool IsDone => _isDone;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect || _isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public void MarkResult(RegistryRepairItemResult result)
    {
        _isSelected = false;
        _isDone = result.Status is RegistryRepairStatus.Repaired or RegistryRepairStatus.SkippedNoLongerInvalid;
        Status = result.Status == RegistryRepairStatus.Repaired
            ? "Repaired (backup saved)"
            : result.Message;
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(IsDone));
    }
}

public sealed class RegistryBackupItemViewModel : ObservableObject
{
    public RegistryBackupItemViewModel(RegistryBackupInfo info, RelayCommand restore)
    {
        Info = info;
        RestoreCommand = restore;
    }

    public RegistryBackupInfo Info { get; }

    public string Location => Info.Location;

    public string CreatedText => Info.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string Problem => Info.IsValid ? string.Empty : Info.Problem ?? "Backup file is invalid.";

    public bool IsValid => Info.IsValid;

    public RelayCommand RestoreCommand { get; }
}

/// <summary>
/// The only place registry changes can be started. Nothing is selected by default, each repair needs an
/// explicit confirmation, and every repaired entry has a verified backup that can be restored from here.
/// </summary>
public sealed class RegistryReviewViewModel : PageViewModel
{
    private readonly PresentationContext _ctx;
    private readonly Action<int> _onRepaired;
    private CancellationTokenSource? _cts;
    private bool _isBusy;
    private string _message = string.Empty;
    private int _selectedCount;
    private double _progress;

    public RegistryReviewViewModel(PresentationContext ctx, IReadOnlyList<RegistryIssue> issues, Action back, Action<int> onRepaired)
        : base("Registry review", back)
    {
        _ctx = ctx;
        _onRepaired = onRepaired;
        var elevated = ctx.Elevation.IsElevated;
        Issues = issues.Select(i => new RegistryIssueItemViewModel(i, elevated, RecalculateSelection)).ToList();
        Backups = new ObservableCollection<RegistryBackupItemViewModel>();
        RepairCommand = new AsyncRelayCommand(RepairAsync, () => !IsBusy && SelectedCount > 0, OnError);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
        SelectAllCommand = new RelayCommand(() => SetAll(true), () => !IsBusy);
        SelectNoneCommand = new RelayCommand(() => SetAll(false), () => !IsBusy);
        RefreshBackups();
    }

    public IReadOnlyList<RegistryIssueItemViewModel> Issues { get; }

    public ObservableCollection<RegistryBackupItemViewModel> Backups { get; }

    public AsyncRelayCommand RepairCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public bool HasIssues => Issues.Count > 0;

    public bool HasBackups => Backups.Count > 0;

    public string Intro => Issues.Count == 0
        ? "No registry issues are being reviewed. Restore a previous repair below if needed."
        : "Clenzy only reports entries that point to a program that is verifiably missing. Nothing is selected by default. Before each change a backup of that single entry is saved and verified.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string RepairText => SelectedCount == 0 ? "Repair selected" : $"Repair selected ({SelectedCount})";

    protected override void GoBack()
    {
        if (!IsBusy)
        {
            base.GoBack();
        }
    }

    private void SetAll(bool value)
    {
        foreach (var item in Issues.Where(i => i.CanSelect))
        {
            item.IsSelected = value;
        }
    }

    private void RecalculateSelection()
    {
        SelectedCount = Issues.Count(i => i.IsSelected);
        OnPropertyChanged(nameof(RepairText));
        RepairCommand.RaiseCanExecuteChanged();
    }

    private void RefreshCommands()
    {
        RepairCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
        foreach (var backup in Backups)
        {
            backup.RestoreCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnError(Exception ex)
    {
        _ctx.Log.Error("Registry review operation failed", ex);
        Message = ex is EngineBusyException ? ex.Message : "The operation could not be completed: " + ex.Message;
        IsBusy = false;
    }

    private async Task RepairAsync()
    {
        var selected = Issues.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var settings = _ctx.Settings.Current;
        var machineWide = selected.Count(i => i.Issue.RequiresElevation);
        var message = $"Clenzy will back up and then remove {selected.Count} registry entr{(selected.Count == 1 ? "y" : "ies")}.\n\n" +
                      "Each entry is saved to a verified backup first and re-checked right before it is changed. " +
                      "You can restore entries from this page afterwards." +
                      (machineWide > 0 ? $"\n\n{machineWide} of these are machine-wide (HKEY_LOCAL_MACHINE)." : string.Empty) +
                      (settings.CreateRestorePoint ? "\n\nA Windows restore point will be attempted first; it may be skipped if System Protection is off." : "\n\nNo restore point will be created (disabled in Settings).");

        if (!await _ctx.Dialogs.ConfirmAsync("Repair registry entries?", message, "Back up and repair").ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        Progress = 0;
        Message = "Repairing...";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new DispatchedProgress<(int Done, int Total)>(_ctx.Dispatcher, p => Progress = p.Total == 0 ? 0 : (double)p.Done / p.Total);
            var report = await Task.Run(() => _ctx.RegistryRepair.RepairAsync(selected.Select(s => s.Issue).ToList(), settings.CreateRestorePoint, progress, _cts.Token)).ConfigureAwait(true);

            foreach (var result in report.Items)
            {
                Issues.FirstOrDefault(i => ReferenceEquals(i.Issue, result.Issue))?.MarkResult(result);
            }

            Message = Summarize(report);
            SaveSession(report);
            RecalculateSelection();
            RefreshBackups();
            _onRepaired(report.Repaired);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private static string Summarize(RegistryRepairReport report)
    {
        var text = $"{report.Repaired} repaired";
        if (report.NotRepaired > 0)
        {
            text += $", {report.NotRepaired} not repaired (see each entry for the reason)";
        }

        if (!string.IsNullOrWhiteSpace(report.RestorePointMessage))
        {
            text += ". " + report.RestorePointMessage;
        }

        return text;
    }

    private void SaveSession(RegistryRepairReport report)
    {
        try
        {
            var now = _ctx.Time.GetUtcNow();
            var record = new SessionRecord
            {
                ScanTimestamp = now,
                Mode = "Registry repair",
                WasElevated = _ctx.Elevation.IsElevated,
                RegistryIssuesDetected = report.Items.Count,
                RegistryIssuesRepaired = report.Repaired
            };
            foreach (var item in report.Items)
            {
                record.RegistryChanges.Add(new SessionRegistryChange
                {
                    Location = item.Issue.DisplayLocation,
                    Rule = item.Issue.Rule.ToString(),
                    Status = item.Status == RegistryRepairStatus.Repaired ? "Repaired" : item.Status + ": " + item.Message,
                    BackupId = item.BackupId
                });
            }

            _ctx.History.Save(record);
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Could not save registry repair history", ex);
        }
    }

    public void RefreshBackups()
    {
        Backups.Clear();
        IReadOnlyList<RegistryBackupInfo> list;
        try
        {
            list = _ctx.RegistryRepair.ListBackups();
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Could not list registry backups", ex);
            list = Array.Empty<RegistryBackupInfo>();
        }

        foreach (var info in list.OrderByDescending(b => b.CreatedAt))
        {
            var captured = info;
            var command = new RelayCommand(() => _ = RestoreAsync(captured), () => !IsBusy && captured.IsValid);
            Backups.Add(new RegistryBackupItemViewModel(info, command));
        }

        OnPropertyChanged(nameof(HasBackups));
    }

    public async Task RestoreAsync(RegistryBackupInfo info)
    {
        if (IsBusy)
        {
            return;
        }

        var confirmed = await _ctx.Dialogs.ConfirmAsync(
            "Restore registry entry?",
            $"Restore this entry from backup?\n\n{info.Location}\n\nIf the entry now exists with different data, it is left untouched.",
            "Restore").ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _ctx.RegistryRepair.Restore(info.BackupId)).ConfigureAwait(true);
            Message = result.Message;
        }
        catch (EngineBusyException ex)
        {
            Message = ex.Message;
        }
        catch (Exception ex)
        {
            _ctx.Log.Error("Registry restore failed", ex);
            Message = "Restore failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
