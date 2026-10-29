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
    public string Owner { get; set; } = Environment.UserDomainName + "\\" + Environment.UserName;
    public string Machine { get; set; } = Environment.MachineName;
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
    public static readonly IReadOnlyList<OptimizationAction> Actions = MaintenanceCatalog.Entries.Select(x => x.Action).ToArray();
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
            if ((action.Irreversible || MaintenanceCatalog.Get(id).Destructive) && !destructiveConfirmed) throw new InvalidOperationException("Destructive changes require a separate confirmation.");
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
            if (ids.Any(id => OptimizationCatalog.Get(id).Irreversible || MaintenanceCatalog.Get(id).Destructive) && !await backend.VerifyRecoveryPointAsync(token))
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
            if (tx.Id != id || tx.Sandbox != backend.Sandbox || tx.Owner != Environment.UserDomainName + "\\" + Environment.UserName || tx.Machine != Environment.MachineName) throw new InvalidOperationException("Backup belongs to a different environment.");
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
    private readonly Dictionary<string, OptimizationState> values = MaintenanceCatalog.Entries.ToDictionary(x => x.Id, x => x.Kind switch {
        "Package" => new OptimizationState(true, "fixture-package", "Package"),
        "Startup" => new OptimizationState(true, "fixture.exe", "String"),
        "Service" => new OptimizationState(true, "Automatic", "Service"),
        _ => new OptimizationState(true, x.Desired == 0 ? "1" : "0", "DWord")
    });
    public Task<OptimizationAudit> AuditAsync(CancellationToken token) => Task.FromResult(new OptimizationAudit("SANDBOX PREVIEW · generated Windows 11 fixtures", true, false,
        OptimizationCatalog.Actions.Select(x => new OptimizationFinding(x, values[x.Id].Value ?? "Absent", true, State: values[x.Id])).ToArray(), "Generated fixtures only. No host hardware, applications, security or performance was inspected. No Windows settings are modified."));
    public Task<OptimizationState> ReadAsync(string id, CancellationToken token) { OptimizationCatalog.Get(id); token.ThrowIfCancellationRequested(); return Task.FromResult(values[id]); }
    public Task<OptimizationState> DesiredAsync(string id, CancellationToken token) => Task.FromResult(MaintenanceCatalog.Get(id).DesiredState);
    public Task WriteAsync(string id, OptimizationState state, CancellationToken token) { OptimizationCatalog.Get(id); token.ThrowIfCancellationRequested(); values[id] = state; return Task.CompletedTask; }
    public Task<bool> VerifyRecoveryPointAsync(CancellationToken token) => Task.FromResult(true);
}
