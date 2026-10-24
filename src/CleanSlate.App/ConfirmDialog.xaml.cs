using System.Windows;
using System.Windows.Interop;

namespace CleanSlate.App;

/// <summary>A themed modal dialog. Cancel is the default button so Enter never confirms a destructive action.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText, string? cancelText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        if (cancelText is null)
        {
            ConfirmButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = confirmText;
        }
        else
        {
            CancelButton.Content = cancelText;
        }

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var dark = ThemeManager.IsDark ? 1 : 0;
            if (CleanSlate.Windows.DwmInterop.SetAttribute(handle, 20, dark) != 0)
            {
                CleanSlate.Windows.DwmInterop.SetAttribute(handle, 19, dark);
            }
        };
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
