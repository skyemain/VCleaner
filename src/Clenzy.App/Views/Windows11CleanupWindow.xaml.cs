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
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clenzy", "Windows11");
    private List<Choice> choices = new();
    private string ReportsRoot => Path.Combine(root, backend.Sandbox ? "sandbox" : "live");
    private OptimizationProfile SelectedProfile => (OptimizationProfile)Math.Max(0, Profile.SelectedIndex);
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
        if (!backend.Sandbox) throw new InvalidOperationException("Smoke test requires fixture backend.");
        audit = await backend.AuditAsync(default); ShowFindings(); AuditText.Text = audit.Details;
        var tx = await engine.ApplyAsync(audit, new[] { "advertising" }, OptimizationProfile.Safe, true, false, default);
        if (tx.Changes.Single().Status != "Verified") throw new InvalidOperationException("Fixture apply failed.");
        await engine.RestoreAsync(tx.Id, default);
        Status.Text = "SANDBOX PREVIEW · scan, fixture change and rollback verified. Host settings untouched.";
        Activity.Text = "Generated fixture transaction verified and restored.";
        ShowFindings(); UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(screenshot); encoder.Save(file);
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
        Findings.ItemsSource = choices; Apply.IsEnabled = !busy && audit is { IsWindows11: true };
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
        var selected = choices.Where(x => x.Selected && x.Enabled).Select(x => x.Finding.Action).ToArray();
        if (selected.Length == 0) { Status.Text = "Select the changes you want first."; return; }
        var preview = string.Join("\n\n", selected.Select(x => x.Title + "\n" + x.Impact));
        if (MessageBox.Show(this, (backend.Sandbox ? "SANDBOX PREVIEW: only fixtures change.\n\n" : "Apply these changes to this Windows user?\n\n") + preview, "Review exact changes", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var destructive = selected.Any(x => x.Irreversible);
        var currentAudit = audit ?? throw new InvalidOperationException("Rescan first.");
        var profile = SelectedProfile;
        if (destructive && MessageBox.Show(this, "App removal can delete app data. Reinstalling does not restore that data. Current-user packages only. Live removal is blocked unless a new restore point is verified. Continue?", "Confirm app removal separately", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync(async token => {
            var tx = await Task.Run(() => engine.ApplyAsync(currentAudit, selected.Select(x => x.Id), profile, true, destructive, token), token);
            Activity.AppendText($"{tx.Started:u} · {(tx.Sandbox ? "SANDBOX" : "LIVE")} · {tx.Id}\n" + string.Join("\n", tx.Changes.Select(x => x.Id + ": " + x.Status)) + "\n");
            Status.Text = $"{tx.Changes.Count} changes verified. No restart was forced. Rescan to refresh findings."; audit = null; choices.Clear(); Findings.ItemsSource = null;
        });
    }
    private async void RestoreClick(object sender, RoutedEventArgs e)
    {
        if (Backups.SelectedItem is not string id || busy) return;
        if (MessageBox.Show(this, "Restore reversible settings in this backup? Newer conflicting values will be preserved and reported. Removed apps must be reinstalled from Microsoft Store.", "Review restore", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        await RunAsync(async token => {
            var tx = await Task.Run(() => engine.RestoreAsync(id, token), token);
            Status.Text = "Reversible settings restored and verified. Rescan before applying anything else.";
            Activity.AppendText(string.Join("\n", tx.Changes.Select(x => x.Id + ": " + x.Status)) + "\n"); audit = null; Findings.ItemsSource = null;
        });
    }
    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void DoneClick(object sender, RoutedEventArgs e) { if (!busy) Close(); }
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
