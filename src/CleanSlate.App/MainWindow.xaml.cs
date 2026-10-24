using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CleanSlate.Presentation.ViewModels;

namespace CleanSlate.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<bool> _minimizeToTray;
    private bool _sessionEnding;

    public MainWindow(MainViewModel viewModel, Func<bool> minimizeToTray)
    {
        _viewModel = viewModel;
        _minimizeToTray = minimizeToTray;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            ApplyTitleBarTheme(ThemeManager.IsDark);
        };
        ThemeManager.ThemeChanged += (_, dark) => ApplyTitleBarTheme(dark);
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    public void MarkSessionEnding() => _sessionEnding = true;

    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _viewModel.CurrentPage is PageViewModel page && page.BackCommand.CanExecute(null))
        {
            page.BackCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _minimizeToTray())
        {
            Hide();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_sessionEnding || !_viewModel.IsBusy)
        {
            return;
        }

        e.Cancel = true;
        MessageBox.Show(this, "CleanSlate is still working. Press Cancel first, wait for it to stop, then close the window.",
            "CleanSlate", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ApplyTitleBarTheme(bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var value = dark ? 1 : 0;
            // 20 is DWMWA_USE_IMMERSIVE_DARK_MODE on current builds; 19 on early Windows 10 20H1 builds.
            if (CleanSlate.Windows.DwmInterop.SetAttribute(handle, 20, value) != 0)
            {
                CleanSlate.Windows.DwmInterop.SetAttribute(handle, 19, value);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Title bar theming is cosmetic.
        }
    }
}
