using System.Windows.Controls;

namespace Clenzy.App.Views;

public partial class HomeView : UserControl
{
    private void Windows11CleanupClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is Clenzy.Presentation.ViewModels.MainViewModel model && model.IsBusy) return;
        var window = new Windows11CleanupWindow { Owner = System.Windows.Window.GetWindow(this) };
        window.ShowDialog();
        if (DataContext is Clenzy.Presentation.ViewModels.MainViewModel current) {
            if (window.DiskCleanupRequested) _ = current.ReviewWindows11DiskCleanupAsync();
            if (window.AdministratorRequested && current.RestartAsAdministratorCommand.CanExecute(null)) current.RestartAsAdministratorCommand.Execute(null);
        }
    }

    public HomeView()
    {
        InitializeComponent();
    }
}
