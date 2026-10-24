using CleanSlate.Presentation.Infrastructure;

namespace CleanSlate.Presentation.ViewModels;

/// <summary>Base class for the pages that replace the home content inside the single main window.</summary>
public abstract class PageViewModel : ObservableObject
{
    private readonly Action _back;

    protected PageViewModel(string title, Action back)
    {
        Title = title;
        _back = back;
        BackCommand = new RelayCommand(GoBack);
    }

    public string Title { get; }

    public RelayCommand BackCommand { get; }

    protected virtual void GoBack() => _back();
}
