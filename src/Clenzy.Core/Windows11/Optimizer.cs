using System.Text.Json;

namespace Clenzy.Core.Windows11;

public enum OptimizationProfile { Safe, Balanced, Advanced }
public sealed record OptimizationAction(string Id, string Category, string Title, string Impact, OptimizationProfile Profile, bool Irreversible = false);
public sealed record OptimizationFinding(OptimizationAction Action, string Current, bool Available, string? BlockedReason = null, OptimizationState? State = null);
public sealed record OptimizationAudit(string Summary, bool IsWindows11, bool IsAdministrator, IReadOnlyList<OptimizationFinding> Findings, string Details);
public sealed record OptimizationState(bool Exists, string? Value, string Kind);
public sealed record OptimizationChange(string Id, OptimizationState Before, OptimizationState After, string Status);
public sealed class OptimizationTransaction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    public string Audit { get; set; } = "";
    public bool Sandbox { get; set; }
    public List<OptimizationChange> Changes { get; set; } = new();
}
public interface IOptimizationBackend
{
    bool Sandbox { get; }
    Task<OptimizationAudit> AuditAsync(CancellationToken token);
    Task<OptimizationState> ReadAsync(string id, CancellationToken token);
    Task<OptimizationState> DesiredAsync(string id, CancellationToken token);
    Task WriteAsync(string id, OptimizationState state, CancellationToken token);
    Task<bool> VerifyRecoveryPointAsync(CancellationToken token);
}

public static class OptimizationCatalog
{
    public static readonly IReadOnlyList<OptimizationAction> Actions = new[] {
        new OptimizationAction("advertising", "Windows Privacy", "Turn off advertising ID", "Stops apps using the Windows advertising ID. Other tracking methods are unaffected.", OptimizationProfile.Safe),
        new OptimizationAction("extensions", "Windows Explorer", "Show file extensions", "Shows known file extensions. Reversible; refresh Explorer if needed.", OptimizationProfile.Safe),
        new OptimizationAction("hidden", "Windows Explorer", "Show hidden files", "Shows hidden files, preserving protected operating-system file hiding.", OptimizationProfile.Balanced),
        new OptimizationAction("explorerads", "Ads and AI Features", "Turn off Explorer provider suggestions", "Suppresses sync-provider promotional notifications. Does not stop syncing.", OptimizationProfile.Safe),
        new OptimizationAction("taskview", "Windows Explorer", "Hide Task View button", "Hides the button; virtual desktops remain available with keyboard shortcuts.", OptimizationProfile.Balanced),
        new OptimizationAction("alignment", "Windows Explorer", "Align taskbar to the left", "Changes taskbar alignment; no shell replacement or restart is forced.", OptimizationProfile.Balanced),
        new OptimizationAction("theme", "Windows Explorer", "Use dark app mode", "Changes the current user's application theme.", OptimizationProfile.Safe),
        new OptimizationAction("Microsoft.BingNews", "Bloatware Removal", "Remove Microsoft News", "Current-user uninstall. Original app data cannot be restored automatically; reinstall from Microsoft Store.", OptimizationProfile.Advanced, true),
        new OptimizationAction("Microsoft.BingWeather", "Bloatware Removal", "Remove Microsoft Weather", "Current-user uninstall; reinstall from Microsoft Store. Does not remove Widgets dependencies.", OptimizationProfile.Advanced, true),
        new OptimizationAction("Microsoft.MicrosoftSolitaireCollection", "Bloatware Removal", "Remove Solitaire Collection", "Current-user uninstall; game data may be lost. Reinstall from Microsoft Store.", OptimizationProfile.Advanced, true),
        new OptimizationAction("Clipchamp.Clipchamp", "Bloatware Removal", "Remove Clipchamp", "Current-user uninstall; export projects first. Reinstall from Microsoft Store.", OptimizationProfile.Advanced, true),
        new OptimizationAction("Microsoft.Getstarted", "Bloatware Removal", "Remove Microsoft Tips", "Only removable installed packages are offered. No provisioned packages are removed.", OptimizationProfile.Advanced, true),
        new OptimizationAction("Microsoft.WindowsFeedbackHub", "Bloatware Removal", "Remove Feedback Hub", "Current-user uninstall. Windows diagnostics and security logging are preserved.", OptimizationProfile.Advanced, true),
        new OptimizationAction("Microsoft.Copilot", "Ads and AI Features", "Remove Copilot app", "Removes only the standalone current-user app; shared AI frameworks, Recall and drivers are preserved.", OptimizationProfile.Advanced, true)
    };
    public static OptimizationAction Get(string id) => Actions.SingleOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Unknown optimization action.");
}

public sealed class OptimizationEngine
{
    private readonly IOptimizationBackend backend;
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);
    public OptimizationEngine(IOptimizationBackend backend, string root) { this.backend = backend; this.root = Path.GetFullPath(root); }
    public async Task<OptimizationTransaction> ApplyAsync(OptimizationAudit audit, IEnumerable<string> selected, OptimizationProfile profile, bool confirmed, bool destructiveConfirmed, CancellationToken token)
    {
        if (!confirmed) throw new InvalidOperationException("Review and confirmation are required.");
        if (!audit.IsWindows11) throw new InvalidOperationException("Windows 11 is required.");
        var ids = selected.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Choose at least one change.");
        foreach (var id in ids) {
            var action = OptimizationCatalog.Get(id);
            if (action.Profile > profile || !audit.Findings.Any(x => x.Action.Id == id && x.Available)) throw new InvalidOperationException("Selection is unavailable or outside the selected profile.");
            if (action.Irreversible && !destructiveConfirmed) throw new InvalidOperationException("App removal requires a separate confirmation.");
        }
        await gate.WaitAsync(token);
        var tx = new OptimizationTransaction { Audit = audit.Details, Sandbox = backend.Sandbox };
        try {
            Directory.CreateDirectory(root);
            // Persist the entire pre-change manifest before the first modification.
            foreach (var id in ids) {
                var before = await backend.ReadAsync(id, token);
                var scanned = audit.Findings.Single(x => x.Action.Id == id).State;
                if (scanned is not null && scanned != before) throw new InvalidOperationException("Setting changed since scan; rescan before applying.");
                tx.Changes.Add(new(id, before, await backend.DesiredAsync(id, token), "Prepared"));
            }
            Save(tx);
            if (ids.Any(id => OptimizationCatalog.Get(id).Irreversible) && !await backend.VerifyRecoveryPointAsync(token))
                throw new InvalidOperationException("No new restore point could be verified. App removal is blocked; use reversible settings or establish recovery first.");
            for (var i = 0; i < tx.Changes.Count; i++) {
                token.ThrowIfCancellationRequested();
                var change = tx.Changes[i];
                try {
                    if (await backend.ReadAsync(change.Id, token) != change.Before) throw new InvalidOperationException("Changed since review; rescan.");
                    tx.Changes[i] = change with { Status = "Applying" }; Save(tx);
                    await backend.WriteAsync(change.Id, change.After, token);
                    if (await backend.ReadAsync(change.Id, token) != change.After) throw new InvalidOperationException("Post-change verification failed.");
                    tx.Changes[i] = change with { Status = "Verified" };
                } catch (Exception ex) {
                    tx.Changes[i] = change with { Status = "Review required: " + ex.Message }; Save(tx); throw;
                }
                Save(tx);
            }
            return tx;
        } finally { try { Save(tx); } finally { gate.Release(); } }
    }
    public async Task<OptimizationTransaction> RestoreAsync(string id, CancellationToken token)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidOperationException("Invalid transaction.");
        await gate.WaitAsync(token);
        try {
            var tx = JsonSerializer.Deserialize<OptimizationTransaction>(File.ReadAllText(Path.Combine(root, id + ".json"))) ?? throw new InvalidDataException();
            if (tx.Id != id || tx.Sandbox != backend.Sandbox) throw new InvalidOperationException("Backup belongs to a different environment.");
            for (var i = tx.Changes.Count - 1; i >= 0; i--) {
                token.ThrowIfCancellationRequested(); var c = tx.Changes[i]; var action = OptimizationCatalog.Get(c.Id);
                if (action.Irreversible || c.Status == "Prepared" || c.Status == "Restored") continue;
                var current = await backend.ReadAsync(c.Id, token);
                if (current == c.Before) { tx.Changes[i] = c with { Status = "Restored" }; Save(tx); continue; }
                if (current != c.After) throw new InvalidOperationException("Setting changed after cleanup; restore stopped to preserve the newer value.");
                await backend.WriteAsync(c.Id, c.Before, token);
                if (await backend.ReadAsync(c.Id, token) != c.Before) throw new InvalidOperationException("Restore verification failed.");
                tx.Changes[i] = c with { Status = "Restored" }; Save(tx);
            }
            return tx;
        } finally { gate.Release(); }
    }
    public IReadOnlyList<string> Transactions() => Directory.Exists(root) ? Directory.GetFiles(root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray() : Array.Empty<string>();
    private void Save(OptimizationTransaction tx) {
        Directory.CreateDirectory(root); var file = Path.Combine(root, tx.Id + ".json"); var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(tx, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, file, true);
    }
}

public sealed class SandboxOptimizationBackend : IOptimizationBackend
{
    public bool Sandbox => true;
    private readonly Dictionary<string, OptimizationState> values = OptimizationCatalog.Actions.ToDictionary(x => x.Id, x => x.Irreversible ? new OptimizationState(true, "fixture-package", "Package") : new OptimizationState(true, "1", "DWord"));
    public Task<OptimizationAudit> AuditAsync(CancellationToken token) => Task.FromResult(new OptimizationAudit("SANDBOX PREVIEW · generated Windows 11 fixtures", true, false,
        OptimizationCatalog.Actions.Select(x => new OptimizationFinding(x, values[x.Id].Value ?? "Absent", true, State: values[x.Id])).ToArray(), "Generated fixtures only. No host hardware, applications, security or performance was inspected. No Windows settings are modified."));
    public Task<OptimizationState> ReadAsync(string id, CancellationToken token) { OptimizationCatalog.Get(id); token.ThrowIfCancellationRequested(); return Task.FromResult(values[id]); }
    public Task<OptimizationState> DesiredAsync(string id, CancellationToken token) => Task.FromResult(OptimizationCatalog.Get(id).Irreversible ? new OptimizationState(false, null, "Package") : new OptimizationState(true, id == "hidden" ? "1" : "0", "DWord"));
    public Task WriteAsync(string id, OptimizationState state, CancellationToken token) { OptimizationCatalog.Get(id); token.ThrowIfCancellationRequested(); values[id] = state; return Task.CompletedTask; }
    public Task<bool> VerifyRecoveryPointAsync(CancellationToken token) => Task.FromResult(true);
}
