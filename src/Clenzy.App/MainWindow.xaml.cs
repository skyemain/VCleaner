using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Clenzy.Presentation.ViewModels;

namespace Clenzy.App;

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
            // Opaque composition avoids rectangular backdrop artifacts on Windows GPUs.
        };
        ThemeManager.ThemeChanged += (_, dark) => ApplyTitleBarTheme(dark);
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
    }

    private void DebloatClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        var page = new Views.Windows11CleanupWindow();
        var content = page.Content; page.Content = null;
        if (content is FrameworkElement element) element.Resources = page.Resources;
        DebloatHost.Content = content; DebloatSurface.Visibility = Visibility.Visible;
        page.EmbeddedOwner = this;
        page.ExitRequested = () => {
            DebloatHost.Content = null; DebloatSurface.Visibility = Visibility.Collapsed;
            if (page.DiskCleanupRequested) _ = _viewModel.ReviewWindows11DiskCleanupAsync();
            if (page.AdministratorRequested && _viewModel.RestartAsAdministratorCommand.CanExecute(null)) _viewModel.RestartAsAdministratorCommand.Execute(null);
        };
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
        MessageBox.Show(this, "Clenzy is still working. Press Cancel first, wait for it to stop, then close the window.",
            "Clenzy", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Turns on the system acrylic (or Mica) behind the window, the "liquid glass" look. Only when Windows accepts it does the
    /// window background become transparent; otherwise the solid theme background stays, so older systems look as before.
    /// </summary>
    private void EnableGlassBackdrop()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero && Clenzy.Windows.DwmInterop.TryEnableGlassBackdrop(handle))
            {
                Background = System.Windows.Media.Brushes.Transparent;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Glass is cosmetic; the solid background stays.
        }
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
            if (Clenzy.Windows.DwmInterop.SetAttribute(handle, 20, value) != 0)
            {
                Clenzy.Windows.DwmInterop.SetAttribute(handle, 19, value);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Title bar theming is cosmetic.
        }
    }
}
