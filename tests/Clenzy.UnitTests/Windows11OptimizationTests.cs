using Clenzy.Core.Windows11;
using Xunit;

namespace Clenzy.UnitTests;

public sealed class Windows11OptimizationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "clenzy-win11-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Fact] public async Task Sandbox_apply_and_restore_changes_only_fixtures_and_saves_manifest()
    {
        var backend = new SandboxOptimizationBackend(); var engine = new OptimizationEngine(backend, root);
        var audit = await backend.AuditAsync(default); var before = await backend.ReadAsync("advertising", default);
        var tx = await engine.ApplyAsync(audit, new[] { "advertising" }, OptimizationProfile.Safe, true, false, default);
        Assert.True(tx.Sandbox); Assert.Equal("Verified", tx.Changes.Single().Status); Assert.NotEqual(before, await backend.ReadAsync("advertising", default));
        Assert.Single(engine.Transactions()); await engine.RestoreAsync(tx.Id, default);
        Assert.Equal(before, await backend.ReadAsync("advertising", default));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Unknown_ids_and_profile_escalation_are_rejected(bool unknown)
    {
        var backend = new SandboxOptimizationBackend(); var engine = new OptimizationEngine(backend, root);
        var audit = await backend.AuditAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync(audit, new[] { unknown ? "Defender" : "Microsoft.Copilot" }, OptimizationProfile.Safe, true, true, default));
        Assert.Empty(engine.Transactions());
    }
    [Fact] public async Task Destructive_and_general_confirmation_are_both_required()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root); var a = await b.AuditAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ApplyAsync(a, new[] { "advertising" }, OptimizationProfile.Safe, false, false, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ApplyAsync(a, new[] { "Microsoft.Copilot" }, OptimizationProfile.Advanced, true, false, default));
    }
    [Fact] public async Task Changed_since_scan_is_rejected()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root); var a = await b.AuditAsync(default);
        await b.WriteAsync("advertising", new(true,"42","DWord"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ApplyAsync(a, new[] { "advertising" }, OptimizationProfile.Safe, true, false, default));
        Assert.Equal("42", (await b.ReadAsync("advertising", default)).Value);
    }
    [Fact] public async Task Restore_preserves_newer_conflicting_value()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root); var a = await b.AuditAsync(default);
        var tx = await e.ApplyAsync(a, new[] { "advertising" }, OptimizationProfile.Safe, true, false, default);
        await b.WriteAsync("advertising", new(true,"42","DWord"), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RestoreAsync(tx.Id, default));
        Assert.Equal("42", (await b.ReadAsync("advertising", default)).Value);
    }
    [Fact] public async Task Cancellation_does_not_write_settings()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root); var a = await b.AuditAsync(default);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => e.ApplyAsync(a,new[]{"advertising"},OptimizationProfile.Safe,true,false,cancel.Token));
        Assert.Equal("1", (await b.ReadAsync("advertising", default)).Value);
    }
    [Fact] public async Task Package_removal_records_its_limited_reversibility()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root); var a = await b.AuditAsync(default);
        var tx = await e.ApplyAsync(a,new[]{"Microsoft.Copilot"},OptimizationProfile.Advanced,true,true,default);
        await e.RestoreAsync(tx.Id, default);
        Assert.False((await b.ReadAsync("Microsoft.Copilot",default)).Exists);
        Assert.True(OptimizationCatalog.Get("Microsoft.Copilot").Irreversible);
    }
    [Fact] public async Task Backup_paths_cannot_escape_transaction_directory()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.RestoreAsync("../other", default));
    }
    [Fact] public async Task Failed_recovery_blocks_package_removal_before_any_write()
    {
        var b = new RecoveryFailureBackend(); var e = new OptimizationEngine(b, root); var a = await b.AuditAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ApplyAsync(a, new[]{"Microsoft.Copilot"}, OptimizationProfile.Advanced, true, true, default));
        Assert.True((await b.ReadAsync("Microsoft.Copilot", default)).Exists);
        Assert.Equal(0, b.Writes); Assert.Single(e.Transactions());
    }
    [Fact] public void Catalog_has_unique_ids_valid_profiles_and_preserves_essential_components()
    {
        Assert.Equal(MaintenanceCatalog.Entries.Count, MaintenanceCatalog.Entries.Select(x => x.Id).Distinct().Count());
        foreach (var entry in MaintenanceCatalog.Entries) {
            Assert.NotEmpty(entry.Impact); Assert.NotEmpty(entry.Title);
            Assert.True(Enum.IsDefined(entry.Action.Profile));
            if (entry.Kind == "Package" || entry.Kind == "Service" || entry.Policy) Assert.Equal(OptimizationProfile.Advanced, entry.Action.Profile);
            Assert.DoesNotContain("Security", entry.Path, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(MaintenanceCatalog.Entries, x => x.Id.Contains("GamingServices") || x.Id.Contains("Xbox") || x.Id.Contains("WindowsStore") || x.Id.Contains("WebExperience"));
    }
    [Fact] public async Task Every_reversible_action_roundtrips_its_exact_prior_state()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root);
        foreach (var entry in MaintenanceCatalog.Entries.Where(x => x.Kind != "Package" && !x.Destructive)) {
            var before = await b.ReadAsync(entry.Id, default);
            var tx = await e.ApplyAsync(await b.AuditAsync(default), new[]{entry.Id}, OptimizationProfile.Advanced, true, false, default);
            Assert.Equal(entry.DesiredState, await b.ReadAsync(entry.Id, default));
            await e.RestoreAsync(tx.Id, default);
            Assert.Equal(before, await b.ReadAsync(entry.Id, default));
        }
    }
    [Fact] public async Task Recall_policy_requires_destructive_consent_even_though_setting_is_reversible()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root);
        var audit = await b.AuditAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ApplyAsync(audit,new[]{"recall"},OptimizationProfile.Advanced,true,false,default));
        Assert.Empty(e.Transactions());
    }
    [Fact] public async Task Startup_command_can_be_restored_after_application_restart()
    {
        var b = new SandboxOptimizationBackend(); var e = new OptimizationEngine(b, root);
        var tx = await e.ApplyAsync(await b.AuditAsync(default),new[]{"startup:Steam"},OptimizationProfile.Balanced,true,false,default);
        Assert.False((await b.ReadAsync("startup:Steam",default)).Exists);
        var restarted = new OptimizationEngine(b,root); await restarted.RestoreAsync(tx.Id,default);
        Assert.Equal("fixture.exe",(await b.ReadAsync("startup:Steam",default)).Value);
    }
    [Fact] public async Task Partial_failure_keeps_first_change_restorable_and_marks_failed_change()
    {
        var b = new PartialFailureBackend(); var e = new OptimizationEngine(b,root);
        var audit = await b.AuditAsync(default);
        await Assert.ThrowsAsync<IOException>(() => e.ApplyAsync(audit,new[]{"advertising","extensions"},OptimizationProfile.Safe,true,false,default));
        Assert.Equal("0",(await b.ReadAsync("advertising",default)).Value);
        Assert.Equal("1",(await b.ReadAsync("extensions",default)).Value);
        var id = Assert.Single(e.Transactions());
        var journal = File.ReadAllText(Path.Combine(root,id+".json"));
        Assert.Contains("Review required",journal);
        await e.RestoreAsync(id,default);
        Assert.Equal("1",(await b.ReadAsync("advertising",default)).Value);
    }
    private sealed class PartialFailureBackend : IOptimizationBackend
    {
        private readonly SandboxOptimizationBackend inner = new();
        public bool Sandbox => true;
        public Task<OptimizationAudit> AuditAsync(CancellationToken token) => inner.AuditAsync(token);
        public Task<OptimizationState> ReadAsync(string id,CancellationToken token) => inner.ReadAsync(id,token);
        public Task<OptimizationState> DesiredAsync(string id,CancellationToken token) => inner.DesiredAsync(id,token);
        public Task<bool> VerifyRecoveryPointAsync(CancellationToken token) => Task.FromResult(true);
        public Task WriteAsync(string id,OptimizationState state,CancellationToken token) => id == "extensions" ? throw new IOException("Simulated denied write") : inner.WriteAsync(id,state,token);
    }
    private sealed class RecoveryFailureBackend : IOptimizationBackend
    {
        private readonly SandboxOptimizationBackend inner = new();
        public int Writes { get; private set; }
        public bool Sandbox => true;
        public Task<OptimizationAudit> AuditAsync(CancellationToken token) => inner.AuditAsync(token);
        public Task<OptimizationState> ReadAsync(string id, CancellationToken token) => inner.ReadAsync(id,token);
        public Task<OptimizationState> DesiredAsync(string id, CancellationToken token) => inner.DesiredAsync(id,token);
        public Task WriteAsync(string id, OptimizationState state, CancellationToken token) { Writes++; return inner.WriteAsync(id,state,token); }
        public Task<bool> VerifyRecoveryPointAsync(CancellationToken token) => Task.FromResult(false);
    }
    [Fact] public void Essential_components_are_not_removal_candidates()
    {
        foreach (var name in new[]{"Microsoft.WindowsStore","Microsoft.DesktopAppInstaller","Microsoft.SecHealthUI","Microsoft.GamingServices","Microsoft.WindowsTerminal","Microsoft.MicrosoftEdge.Stable"})
            Assert.DoesNotContain(OptimizationCatalog.Actions, x => x.Id == name);
    }
}
