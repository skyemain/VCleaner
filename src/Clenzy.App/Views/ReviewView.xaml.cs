using System.Windows;
using System.Windows.Controls;
using Clenzy.Presentation.ViewModels;

namespace Clenzy.App.Views;

public partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => (DataContext as ReviewViewModel)?.SelectAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => (DataContext as ReviewViewModel)?.SelectAll(false);
}
