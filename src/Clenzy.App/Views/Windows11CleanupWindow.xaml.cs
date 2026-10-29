using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Clenzy.Core.Windows11;
using Clenzy.Windows.Windows11;

namespace Clenzy.App.Views;

public partial class Windows11CleanupWindow : Window
{
    private IOptimizationBackend backend = new SandboxOptimizationBackend();
    private OptimizationEngine engine = null!;
    private OptimizationAudit? audit;
    private CancellationTokenSource? operation;
    private bool busy;
    public Window? EmbeddedOwner { get; set; }
    public Action? ExitRequested { get; set; }
    private void Leave() { if (ExitRequested is not null) ExitRequested(); else Close(); }
    public bool DiskCleanupRequested { get; private set; }
    public bool AdministratorRequested { get; private set; }
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clenzy", "Windows11");
    private List<Choice> choices = new();
    private string ReportsRoot => Path.Combine(root, backend.Sandbox ? "sandbox" : "live");
    private OptimizationProfile SelectedProfile => Profile.SelectedIndex == 0 ? OptimizationProfile.Balanced : OptimizationProfile.Advanced;
    public Windows11CleanupWindow()
    {
        InitializeComponent(); ResetEnvironment();

        foreach (var (title, uri) in new[] {
            ("Privacy & permissions", "ms-settings:privacy"), ("Ads & recommendations", "ms-settings:privacy-general"),
            ("Installed apps / OEM software", "ms-settings:appsfeatures"), ("Startup optimization", "ms-settings:startupapps"),
            ("Background permissions", "ms-settings:appsfeatures"), ("Disk cleanup", "ms-settings:storagesense"),
            ("Performance / power", "ms-settings:powersleep"), ("Gaming / Game Mode", "ms-settings:gaming-gamemode"),
            ("Explorer / personalization", "ms-settings:personalization"), ("Security & health", "windowsdefender:"),
            ("Windows Update", "ms-settings:windowsupdate"), ("Recovery", "ms-settings:recovery") }) {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { Status.Text = ex.Message; } };
            SettingsLinks.Children.Add(button);
        }
        Closing += (_, e) => { if (busy) { operation?.Cancel(); e.Cancel = true; Status.Text = "Stopping at the next safe boundary. Close when the operation finishes."; } };
    }
    public async Task RunSandboxSmokeAsync(string screenshot)
    {
        Sandbox.IsChecked = true; ResetEnvironment();
        if (!backend.Sandbox) throw new InvalidOperationException("Smoke test requires fixture backend.");
        audit = await backend.AuditAsync(default); ShowFindings(); AuditText.Text = audit.Details;
        var tx = await engine.ApplyAsync(audit, new[] { "advertising" }, OptimizationProfile.Safe, true, false, default);
        if (tx.Changes.Single().Status != "Verified") throw new InvalidOperationException("Fixture apply failed.");
        await engine.RestoreAsync(tx.Id, default);
        Status.Text = "SANDBOX PREVIEW · scan, fixture change and rollback verified. Host settings untouched.";
        Activity.Text = "Generated fixture transaction verified and restored.";
        ShowFindings(); UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var content = (FrameworkElement)Content; Content = null; content.Resources = Resources;
        var surface = new System.Windows.Controls.Border { Background = (System.Windows.Media.Brush)FindResource("WindowBackgroundBrush"), Child = content };
        if (surface.Background is not System.Windows.Media.SolidColorBrush { Color.A: 255 }) throw new InvalidOperationException("Embedded page must be opaque.");
        var host = new Window { Width = ActualWidth, Height = ActualHeight, Content = surface };
        host.Show(); host.UpdateLayout();
        if (content.TryFindResource(typeof(System.Windows.Controls.TabItem)) is null) throw new InvalidOperationException("Embedded theme was lost.");
        bitmap.Render(host); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(screenshot)) encoder.Save(file);
        host.Content = null; surface.Child = null; host.Close(); Content = content;
    }
    private void EnvironmentChanged(object sender, RoutedEventArgs e) { if (Findings is not null && !busy) ResetEnvironment(); }
    private void ResetEnvironment()
    {
        backend = Sandbox.IsChecked == true ? new SandboxOptimizationBackend() : new WindowsOptimizationBackend();
        engine = new OptimizationEngine(backend, ReportsRoot); audit = null; choices.Clear(); Findings.ItemsSource = null; Apply.IsEnabled = false; AuditText.Text = "";
        Status.Text = backend.Sandbox ? "SANDBOX PREVIEW · generated fixtures only. No host changes." : "LIVE WINDOWS 11 · Scan reads this PC. Applying requires review; app removal needs separate consent and a verified new restore point.";
        Backups.ItemsSource = engine.Transactions();
    }
    private void ProfileChanged(object sender, SelectionChangedEventArgs e) { if (Findings is not null && !busy) ShowFindings(); }
    private void ShowFindings()
    {
        choices = audit?.Findings.Where(x => x.Action.Profile <= SelectedProfile).Select(x => new Choice(x)).ToList() ?? new();
        FilterFindings(); Apply.IsEnabled = !busy && audit is { IsWindows11: true };
    }
    private void FilterChanged(object sender, RoutedEventArgs e) { if (Findings is not null) FilterFindings(); }
    private void FilterFindings()
    {
        var query = Search?.Text?.Trim() ?? "";
        Findings.ItemsSource = choices.Where(x => (AvailableOnly?.IsChecked != true || x.Enabled) && (query.Length == 0 || (x.Title + " " + x.Description).Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
    }
    private void SelectSafeClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        foreach (var choice in choices) choice.Selected = choice.Enabled && choice.Finding.Action.Profile == OptimizationProfile.Safe;
        FilterFindings(); Findings.Items.Refresh();
    }
    private void DiskCleanupClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (backend.Sandbox) { Status.Text = "Disk cleanup operates on real files. Leave sandbox preview to open the existing scan-and-review screen; nothing is deleted without review."; return; }
        DiskCleanupRequested = true; Leave();
    }
    private void AdministratorClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        AdministratorRequested = true; Leave();
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy) return;
        busy = true; operation = new(); Scan.IsEnabled = Apply.IsEnabled = Restore.IsEnabled = Sandbox.IsEnabled = Profile.IsEnabled = false;
        Findings.IsEnabled = false; Cancel.IsEnabled = true; Progress.Visibility = Visibility.Visible;
        try { await action(operation.Token); }
        catch (OperationCanceledException) { Status.Text = "Stopped. Completed changes and recovery records remain in Activity; rescan before trying again."; }
        catch (Exception ex) { Status.Text = ex.Message; Activity.AppendText($"{DateTimeOffset.Now:u} {ex.Message}\n"); }
        finally {
            busy = false; operation.Dispose(); operation = null; Scan.IsEnabled = Restore.IsEnabled = Sandbox.IsEnabled = Profile.IsEnabled = true;
            Findings.IsEnabled = true; Cancel.IsEnabled = false; Progress.Visibility = Visibility.Collapsed; Backups.ItemsSource = engine.Transactions();
            Apply.IsEnabled = audit is { IsWindows11: true };
        }
    }
    private async void ScanClick(object sender, RoutedEventArgs e) => await RunAsync(async token => {
        Status.Text = "Scanning packages, hardware, security and supported settings…";
        audit = await Task.Run(() => backend.AuditAsync(token), token); ShowFindings(); AuditText.Text = audit.Details;
        Directory.CreateDirectory(ReportsRoot);
        await File.WriteAllTextAsync(Path.Combine(ReportsRoot, "audit-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"), System.Text.Json.JsonSerializer.Serialize(audit), token);
        Status.Text = audit.Summary + " · Review availability and impact. No changes applied.";
    });
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (audit is null || busy) return;
        if (!backend.Sandbox && audit.IsAdministrator == false) {
            if (MessageBox.Show(EmbeddedOwner ?? this, "Windows needs administrator permission to apply debloat changes. Restart Clenzy as administrator, then rescan and review your selection.", "Administrator access", MessageBoxButton.OKCancel) == MessageBoxResult.OK) { AdministratorRequested = true; Leave(); }
            return;
        }
        var selected = choices.Where(x => x.Selected && x.Enabled).Select(x => x.Finding.Action).ToArray();
        if (selected.Length == 0) { Status.Text = "Select the changes you want first."; return; }
        var preview = string.Join("\n\n", selected.Select(x => x.Title + "\n" + x.Impact));
        if (MessageBox.Show(EmbeddedOwner ?? this, (backend.Sandbox ? "SANDBOX PREVIEW: only fixtures change.\n\n" : "Apply these changes to this Windows user?\n\n") + preview, "Review exact changes", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var destructive = selected.Any(x => x.Irreversible || MaintenanceCatalog.Get(x.Id).Destructive);
        var currentAudit = audit ?? throw new InvalidOperationException("Rescan first.");
        var profile = SelectedProfile;
        if (destructive && MessageBox.Show(EmbeddedOwner ?? this, "These changes may delete app data or saved Recall snapshots. Restoring settings cannot restore deleted data. A verified new restore point is required for live destructive changes. Continue?", "Confirm destructive changes separately", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync(async token => {
            var tx = await Task.Run(() => engine.ApplyAsync(currentAudit, selected.Select(x => x.Id), profile, true, destructive, token), token);
            Activity.AppendText($"{tx.Started:u} · {(tx.Sandbox ? "SANDBOX" : "LIVE")} · {tx.Id}\n" + string.Join("\n", tx.Changes.Select(x => x.Id + ": " + x.Status)) + "\n");
            Status.Text = $"{tx.Changes.Count} stored changes verified. Windows may need sign-out/restart to reflect policy and shell changes. No restart was forced. Rescan to refresh findings."; audit = null; choices.Clear(); Findings.ItemsSource = null;
        });
    }
    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (Backups.SelectedItem is not string id || busy) return;
        if (MessageBox.Show(EmbeddedOwner ?? this, "Restore reversible settings in this backup? Newer conflicting values will be preserved and reported. Removed apps must be reinstalled from Microsoft Store.", "Review restore", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await RunAsync(async token => {
            var tx = await Task.Run(() => engine.RestoreAsync(id, token), token);
            Status.Text = "Reversible settings restored and verified. Rescan before applying anything else.";
            Activity.AppendText(string.Join("\n", tx.Changes.Select(x => x.Id + ": " + x.Status)) + "\n"); audit = null; Findings.ItemsSource = null;
        });
    }
    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void DoneClick(object sender, RoutedEventArgs e) { if (!busy) Leave(); }
    private void ReportsClick(object sender, RoutedEventArgs e) { Directory.CreateDirectory(ReportsRoot); Process.Start(new ProcessStartInfo(ReportsRoot) { UseShellExecute = true }); }
    private void LaunchSandboxClick(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try { WindowsSandboxLauncher.Launch(Environment.ProcessPath ?? throw new InvalidOperationException("Executable unavailable.")); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
    private sealed class Choice
    {
        public OptimizationFinding Finding { get; }
        public bool Selected { get; set; }
        public bool Enabled => Finding.Available;
        public string Title => Finding.Action.Title;
        public string Description => $"{Finding.Action.Category} · {Finding.Current}\n{Finding.Action.Impact}" + (Finding.BlockedReason is null ? "" : "\n" + Finding.BlockedReason);
        public Choice(OptimizationFinding finding) { Finding = finding; }
    }
}
