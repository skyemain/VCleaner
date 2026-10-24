using CleanSlate.Core.Models;
using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.Presentation.ViewModels;

/// <summary>One of the five rows on the home screen.</summary>
public sealed class CategoryRowViewModel : ObservableObject
{
    private string _status = "Ready to scan";
    private RowKind _kind = RowKind.Neutral;

    public CategoryRowViewModel(CleanerGroup group, string description)
    {
        Group = group;
        Title = GroupStatusFormatter.Title(group);
        Description = description;
    }

    public CleanerGroup Group { get; }

    public string Title { get; }

    public string Description { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public RowKind Kind
    {
        get => _kind;
        private set
        {
            if (SetProperty(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsActive));
            }
        }
    }

    public bool IsActive => Kind == RowKind.Active;

    public string AutomationName => $"{Title}: {Status}";

    public void Set((string Text, RowKind Kind) value)
    {
        Status = value.Text;
        Kind = value.Kind;
        OnPropertyChanged(nameof(AutomationName));
    }
}
