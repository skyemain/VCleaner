using System.Windows;
using System.Windows.Controls;
using CleanSlate.Presentation.ViewModels;

namespace CleanSlate.App.Views;

public partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => (DataContext as ReviewViewModel)?.SelectAll(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => (DataContext as ReviewViewModel)?.SelectAll(false);
}
