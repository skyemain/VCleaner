using System.Windows.Controls;

namespace Clenzy.App.Views;

public partial class HomeView : UserControl
{
    private void Windows11CleanupClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is Clenzy.Presentation.ViewModels.MainViewModel model && model.IsBusy) return;
        new Windows11CleanupWindow { Owner = System.Windows.Window.GetWindow(this) }.ShowDialog();
    }

    public HomeView()
    {
        InitializeComponent();
    }
}
