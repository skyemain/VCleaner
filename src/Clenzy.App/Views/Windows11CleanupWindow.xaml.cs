using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clenzy.Core.Windows11;
using Clenzy.Windows.Windows11;
namespace Clenzy.App.Views;
public partial class Windows11CleanupWindow : Window
{
    private IOptimizationBackend backend = new WindowsOptimizationBackend();
    private OptimizationEngine engine = null!;
    private OptimizationAudit? audit;
    private List<Choice> choices = new();
    private bool busy, started;
    public Window? EmbeddedOwner { get; set; }
    public Action? ExitRequested { get; set; }
    public bool DiskCleanupRequested => false;
    public bool AdministratorRequested => false;
    private string ReportsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clenzy", "Windows11", backend.Sandbox ? "sandbox" : "live");
    public Windows11CleanupWindow()
    {
        InitializeComponent(); engine = new(backend, ReportsRoot);
        Closing += (_, e) => { if (busy) e.Cancel = true; };
    }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        if (started || Environment.GetCommandLineArgs().Contains("--windows11-smoke")) return;
        started = true; await ScanAsync();
    }
    private void SetBusy(bool value)
    {
        busy = value; Findings.IsEnabled = Back.IsEnabled = !value;
        Apply.IsEnabled = !value && audit is { IsWindows11: true } && choices.Count > 0;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task ScanAsync()
    {
        SetBusy(true);
        try {
            Status.Text = "Scanning installed apps, privacy settings, startup and optional services…";
            audit = await Task.Run(() => backend.AuditAsync(default));
            choices = audit.Findings.Where(x => x.Available).Select(x => new Choice(x)).ToList(); Findings.ItemsSource = choices;
            Directory.CreateDirectory(ReportsRoot);
            await File.WriteAllTextAsync(Path.Combine(ReportsRoot, "audit-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"), JsonSerializer.Serialize(audit));
            Status.Text = audit.IsWindows11 ? $"{choices.Count} available changes. Select the ones you want to apply." : audit.Summary;
        } catch (Exception ex) { Status.Text = "Scan could not complete: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (busy || audit is null) return;
        var selected = choices.Where(x => x.Selected).Select(x => x.Finding.Action).ToArray();
        if (selected.Length == 0) { Status.Text = "Select at least one change."; return; }
        var destructive = selected.Any(x => x.Irreversible || MaintenanceCatalog.Get(x.Id).Destructive);
        if (destructive && MessageBox.Show(EmbeddedOwner ?? this, "Selected changes can uninstall apps and delete their data or saved Recall snapshots. Export anything you need first. Continue with these selected changes?", "Confirm selected removals", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var old = engine.Transactions().ToHashSet();
        SetBusy(true); Status.Text = "Creating and verifying a restore point and backing up settings…";
        OptimizationTransaction? transaction = null; string? failure = null;
        try {
            transaction = await Task.Run(() => engine.ApplyAsync(audit, selected.Select(x => x.Id), OptimizationProfile.Advanced, true, destructive, default, requireRecoveryPoint: true));
        } catch (Exception ex) {
            failure = ex.Message;
            var id = engine.Transactions().FirstOrDefault(x => !old.Contains(x));
            if (id is not null) {
                try { transaction = JsonSerializer.Deserialize<OptimizationTransaction>(await File.ReadAllTextAsync(Path.Combine(ReportsRoot,id+".json"))); }
                catch (Exception readError) { failure += " Backup report could not be read: " + readError.Message; }
            }
        }
        ShowReport(transaction, selected.Select(x => x.Id).ToArray(), failure);
        audit = null; SetBusy(false); Apply.Visibility = Visibility.Collapsed;
    }
    private void ShowReport(OptimizationTransaction? tx, string[] selected, string? failure)
    {
        var changed = tx?.Changes.Count(x => x.Status == "Verified") ?? 0;
        Status.Text = failure is null ? $"Finished · {changed} changes verified." : "Stopped · " + failure;
        var lines = new List<string>();
        foreach (var id in selected) {
            var c = tx?.Changes.FirstOrDefault(x => x.Id == id);
            var outcome = c?.Status == "Verified" ? "Changed · verified" : c is null || c.Status == "Prepared" ? "Not changed · not applied" : "Not verified · " + c.Status;
            lines.Add(outcome + " — " + OptimizationCatalog.Get(id).Title);
        }
        foreach (var c in choices.Where(x => !selected.Contains(x.Finding.Action.Id))) lines.Add("Not changed · not selected — " + c.Title);
        lines.Add("\nSettings backups and the full audit are saved locally. Removed app data and Recall snapshots cannot be restored from the settings backup. Windows may require sign-out or restart.");
        ReportText.Text = string.Join("\n\n", lines); Findings.Visibility = Visibility.Collapsed; Report.Visibility = Visibility.Visible;
        try { File.WriteAllText(Path.Combine(ReportsRoot,"report-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt"),Status.Text+"\n\n"+ReportText.Text); }
        catch (Exception ex) { Status.Text += " Report could not be saved: " + ex.Message; }
    }
    private void DoneClick(object sender, RoutedEventArgs e) { if (!busy) { if (ExitRequested is not null) ExitRequested(); else Close(); } }
    public async Task RunSandboxSmokeAsync(string screenshot)
    {
        started = true; backend = new SandboxOptimizationBackend(); engine = new(backend, ReportsRoot);
        await ScanAsync();
        if (choices.Count == 0 || choices.Any(x => !x.Finding.Available)) throw new InvalidOperationException("Available-only scan failed.");
        var tx = await engine.ApplyAsync(audit!,new[]{"advertising"},OptimizationProfile.Advanced,true,false,default,requireRecoveryPoint:true);
        ShowReport(tx,new[]{"advertising"},null);
        if (!ReportText.Text.Contains("Changed · verified")) throw new InvalidOperationException("Report missing verified result.");
        await engine.RestoreAsync(tx.Id,default);
        var content = (FrameworkElement)Content; Content = null;
        var surface = new System.Windows.Controls.Border { Background=(Brush)FindResource("WindowBackgroundBrush"), Child=content };
        if (surface.Background is not SolidColorBrush { Color.A:255 }) throw new InvalidOperationException("Embedded page must be opaque.");
        var host = new Window {Width=1040,Height=780,Content=surface}; host.Show(); host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1040,780,96,96,PixelFormats.Pbgra32); bitmap.Render(host);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file=File.Create(screenshot)) encoder.Save(file);
        host.Content=null; surface.Child=null; host.Close(); Content=content;
    }
    private sealed class Choice
    {
        public OptimizationFinding Finding { get; }
        public bool Selected { get; set; }
        public string Title => Finding.Action.Title;
        public string Description => Finding.Action.Category + " · " + Finding.Action.Impact;
        public Choice(OptimizationFinding finding) { Finding=finding; }
    }
}
