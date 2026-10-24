using System.Collections.ObjectModel;
using CleanSlate.Core.Models;
using CleanSlate.Core.Services;
using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.Presentation.ViewModels;

public sealed class ReviewItemViewModel : ObservableObject
{
    private readonly List<Finding> _actionable;
    private readonly Action _changed;
    private bool _isSelected;

    public ReviewItemViewModel(string moduleName, string category, IReadOnlyList<Finding> findings, Action changed)
    {
        _changed = changed;
        ModuleName = moduleName;
        Category = category;
        _actionable = findings.Where(f => f.IsEligible && f.Action != ActionType.None).ToList();
        var first = findings[0];
        Risk = findings.Max(f => f.Risk);
        IsPermanent = first.IsPermanent;
        NeedsConfirmation = _actionable.Any(f => f.ConfirmationRequired);
        SkipReason = _actionable.Count == 0 ? findings.FirstOrDefault(f => !f.IsEligible)?.SkipReason : null;
        Count = _actionable.Count;
        Bytes = _actionable.Sum(f => f.EstimatedBytes);
        _isSelected = _actionable.Count > 0 && _actionable.Any(f => f.IsSelected);
        ApplySelection(_isSelected);
    }

    public string ModuleName { get; }

    public string Category { get; }

    public int Count { get; }

    public long Bytes { get; }

    public RiskLevel Risk { get; }

    public bool IsPermanent { get; }

    public bool NeedsConfirmation { get; }

    public string? SkipReason { get; }

    public bool CanSelect => Count > 0;

    public string SizeText => Count == 0 ? "—" : Bytes > 0 ? ByteFormatter.Format(Bytes) : $"{Count:N0} item{(Count == 1 ? string.Empty : "s")}";

    public string CountText => Count == 0 ? SkipReason ?? "Nothing to clean" : $"{Count:N0} item{(Count == 1 ? string.Empty : "s")}";

    public string RiskText => Risk switch
    {
        RiskLevel.Safe => "Safe",
        RiskLevel.Low => "Low risk",
        RiskLevel.Medium => "Medium risk",
        _ => "High risk"
    };

    public string Note => NeedsConfirmation
        ? (IsPermanent ? "Needs your confirmation. Cannot be undone." : "Needs your confirmation.")
        : IsPermanent ? "Permanently deleted (not sent to the Recycle Bin)." : string.Empty;

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
            ApplySelection(value);
            OnPropertyChanged();
            _changed();
        }
    }

    private void ApplySelection(bool value)
    {
        foreach (var finding in _actionable)
        {
            finding.IsSelected = value;
        }
    }
}

public sealed class ReviewSectionViewModel
{
    public ReviewSectionViewModel(string title, IReadOnlyList<ReviewItemViewModel> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }

    public IReadOnlyList<ReviewItemViewModel> Items { get; }
}

/// <summary>
/// Shown for "Review Before Cleaning" and automatically whenever the scan contains an operation that
/// must be confirmed (permanent or user-visible). Nothing runs until the user confirms here.
/// </summary>
public sealed class ReviewViewModel : PageViewModel
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReviewViewModel(ScanReport scan, bool forcedByPolicy, Action<ReviewViewModel> onFinished)
        : base("Review before cleaning", () => { })
    {
        ForcedByPolicy = forcedByPolicy;
        var sections = new List<ReviewSectionViewModel>();
        foreach (var group in scan.Modules.Where(m => m.Group != CleanerGroup.Registry).GroupBy(m => m.Group).OrderBy(g => g.Key))
        {
            var items = new List<ReviewItemViewModel>();
            foreach (var module in group)
            {
                foreach (var category in module.Findings.Where(f => f.Action != ActionType.RepairRegistryEntry).GroupBy(f => f.Category))
                {
                    items.Add(new ReviewItemViewModel(module.ModuleName, category.Key, category.ToList(), Recalculate));
                }
            }

            if (items.Count > 0)
            {
                sections.Add(new ReviewSectionViewModel(GroupStatusFormatter.Title(group.Key), items));
            }
        }

        Sections = sections;
        AllItems = sections.SelectMany(s => s.Items).ToList();
        ConfirmCommand = new RelayCommand(() => Finish(true), () => SelectedCount > 0);
        CancelCommand = new RelayCommand(() => Finish(false));
        _onFinished = onFinished;
        Recalculate();
    }

    private readonly Action<ReviewViewModel> _onFinished;
    private long _selectedBytes;
    private int _selectedCount;
    private bool _hasPermanentSelection;
    private bool _finished;

    public IReadOnlyList<ReviewSectionViewModel> Sections { get; }

    public IReadOnlyList<ReviewItemViewModel> AllItems { get; }

    public bool ForcedByPolicy { get; }

    public string Intro => ForcedByPolicy
        ? "Some selected operations are permanent or change data you may care about, so CleanSlate needs your confirmation first."
        : "Choose exactly what to clean. Nothing is deleted until you confirm.";

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand CancelCommand { get; }

    public Task<bool> Completion => _completion.Task;

    public long SelectedBytes
    {
        get => _selectedBytes;
        private set => SetProperty(ref _selectedBytes, value);
    }

    public int SelectedCount
    {
        get => _selectedCount;
        private set => SetProperty(ref _selectedCount, value);
    }

    public bool HasPermanentSelection
    {
        get => _hasPermanentSelection;
        private set => SetProperty(ref _hasPermanentSelection, value);
    }

    public string ConfirmText => SelectedCount == 0
        ? "Nothing selected"
        : SelectedBytes > 0 ? $"Clean selected ({ByteFormatter.Format(SelectedBytes)})" : $"Clean selected ({SelectedCount:N0} items)";

    public string SummaryText => SelectedCount == 0
        ? "No categories selected."
        : $"{SelectedCount:N0} item{(SelectedCount == 1 ? string.Empty : "s")} selected" + (SelectedBytes > 0 ? $" · about {ByteFormatter.Format(SelectedBytes)}" : string.Empty);

    public string PermanentWarning => "Selected items are deleted permanently. They are not moved to the Recycle Bin and cannot be restored by CleanSlate.";

    protected override void GoBack() => Finish(false);

    public void SelectAll(bool selected)
    {
        foreach (var item in AllItems.Where(i => i.CanSelect))
        {
            item.IsSelected = selected;
        }
    }

    private void Recalculate()
    {
        var selected = AllItems.Where(i => i.CanSelect && i.IsSelected).ToList();
        SelectedCount = selected.Sum(i => i.Count);
        SelectedBytes = selected.Sum(i => i.Bytes);
        HasPermanentSelection = selected.Any(i => i.IsPermanent);
        OnPropertyChanged(nameof(ConfirmText));
        OnPropertyChanged(nameof(SummaryText));
        ConfirmCommand?.RaiseCanExecuteChanged();
    }

    private void Finish(bool confirmed)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _completion.TrySetResult(confirmed);
        _onFinished(this);
    }
}
