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
    [Fact] public void Essential_components_are_not_removal_candidates()
    {
        foreach (var name in new[]{"Microsoft.WindowsStore","Microsoft.DesktopAppInstaller","Microsoft.SecHealthUI","Microsoft.GamingServices","Microsoft.WindowsTerminal","Microsoft.MicrosoftEdge.Stable"})
            Assert.DoesNotContain(OptimizationCatalog.Actions, x => x.Id == name);
    }
}
