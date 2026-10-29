using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Clenzy.Core.Windows11;

namespace Clenzy.Windows.Windows11;

public sealed class WindowsOptimizationBackend : IOptimizationBackend
{
    public bool Sandbox => false;
    private static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
        && string.Equals(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\ProductOptions", "ProductType", null) as string, "WinNT", StringComparison.OrdinalIgnoreCase);
    private static bool Administrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    private static RegistryKey Hive(MaintenanceEntry entry) => entry.Machine ? Registry.LocalMachine : Registry.CurrentUser;
    private static string? Compatibility(MaintenanceEntry entry)
    {
        if (!Supported) return "Windows 11 client is required.";
        if (entry.Machine && !Administrator) return "Administrator required. Use Restart as administrator, then scan again.";
        var build = Environment.OSVersion.Version.Build;
        var revision = Convert.ToInt32(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", 0));
        if (build < entry.MinBuild || (build == entry.MinBuild && revision < entry.MinRevision)) return "Not supported on this Windows build/update.";
        if (entry.Policy) {
            var edition = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", "") as string ?? "";
            if (!(edition.StartsWith("Professional", StringComparison.Ordinal) || edition.StartsWith("Enterprise", StringComparison.Ordinal) || edition.StartsWith("Education", StringComparison.Ordinal)))
                return "Policy not supported by this edition; use Windows Settings.";
            using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
            if (enrollments?.GetSubKeyNames().Any(x => { using var k = enrollments.OpenSubKey(x); return k?.GetValue("ProviderID") is not null; }) == true)
                return "Managed device policy is preserved.";
            using var groupPolicy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\History");
            if (groupPolicy?.GetSubKeyNames().Length > 0) return "Existing group policy management is preserved.";
        }
        return null;
    }
    public async Task<OptimizationAudit> AuditAsync(CancellationToken token)
    {
        if (!Supported) return new("Windows 11 is required for live changes.", false, false, Array.Empty<OptimizationFinding>(), "No live scan was run.");
        var audit = await InvokeAsync(new { Operation = "Audit" }, token);
        var packages = await InvokeAsync(new { Operation = "Packages" }, token);
        var findings = new List<OptimizationFinding>();
        foreach (var entry in MaintenanceCatalog.Entries) {
            token.ThrowIfCancellationRequested();
            try {
                string? reason = Compatibility(entry);
                OptimizationState state;
                if (entry.Kind == "Package") {
                    var p = packages.GetProperty(entry.Id);
                    var exists = p.GetProperty("Exists").GetBoolean();
                    state = new(exists, exists ? p.GetProperty("FullName").GetString() : null, "Package");
                    if (!exists || p.GetProperty("Blocked").GetBoolean()) reason = p.GetProperty("Reason").GetString();
                    if (!Administrator && exists) reason = "Removal needs administrator rights to create and verify a restore point. Restart as administrator.";
                } else if (entry.Kind == "Service") {
                    var p = await InvokeAsync(new { Operation = "Service", Id = entry.Id }, token);
                    state = new(p.GetProperty("Exists").GetBoolean(), p.GetProperty("Mode").GetString(), "Service");
                    if (!p.GetProperty("Available").GetBoolean()) reason = p.GetProperty("Reason").GetString();
                } else {
                    state = await ReadAsync(entry.Id, token);
                    if (!state.Exists && !entry.AllowCreate) reason = "No existing supported setting or startup entry on this profile.";
                    if (state.Exists && state.Kind != (entry.Kind == "Startup" ? "String" : "DWord")) reason = "Unsupported value type; preserved.";
                    if (entry.Policy && state.Exists && state != entry.DesiredState) reason = "An existing policy is configured; preserved. Review it in Windows Settings or with your administrator.";
                    if (entry.Policy && audit.TryGetProperty("DomainJoined", out var domain) && domain.GetBoolean()) reason = "Domain-managed policies are preserved.";
                }
                if (state == entry.DesiredState) reason = "Already configured; no change needed.";
                var current = entry.Kind == "Package" ? (state.Exists ? "Installed" : "Not installed") : entry.Kind == "Startup" ? (state.Exists ? "Starts at sign-in" : "No Run entry") : state.Exists ? state.Value ?? "Unknown" : "Not configured";
                findings.Add(new(entry.Action, current, reason is null, reason, state));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                findings.Add(new(entry.Action, "Could not inspect", false, ex.Message));
            }
        }
        var os = audit.GetProperty("OS");
        return new($"{os.GetProperty("Caption").GetString()} · build {os.GetProperty("BuildNumber").GetString()} · {os.GetProperty("OSArchitecture").GetString()}", true,
            Administrator, findings, JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
    }
    public async Task<OptimizationState> ReadAsync(string id, CancellationToken token)
    {
        var entry = MaintenanceCatalog.Get(id); token.ThrowIfCancellationRequested();
        if (entry.Kind == "Package") {
            var p = await InvokeAsync(new { Operation = "Package", Id = id }, token); var exists = p.GetProperty("Exists").GetBoolean();
            return new(exists, exists ? p.GetProperty("FullName").GetString() : null, "Package");
        }
        if (entry.Kind == "Service") {
            var p = await InvokeAsync(new { Operation = "Service", Id = id }, token);
            return new(p.GetProperty("Exists").GetBoolean(), p.GetProperty("Mode").GetString(), "Service");
        }
        using var key = Hive(entry).OpenSubKey(entry.Path);
        var kind = entry.Kind == "Startup" ? "String" : "DWord";
        if (key is null || !key.GetValueNames().Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) return new(false, null, kind);
        return new(true, key.GetValue(entry.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString(), key.GetValueKind(entry.Name).ToString());
    }
    public Task<OptimizationState> DesiredAsync(string id, CancellationToken token) => Task.FromResult(MaintenanceCatalog.Get(id).DesiredState);
    public async Task WriteAsync(string id, OptimizationState state, CancellationToken token)
    {
        var entry = MaintenanceCatalog.Get(id); token.ThrowIfCancellationRequested();
        var reason = Compatibility(entry); if (reason is not null) throw new InvalidOperationException(reason);
        if (entry.Kind == "Package") {
            if (state.Exists) throw new InvalidOperationException("Reinstall from Microsoft Store. Original app data cannot be restored automatically.");
            var before = await ReadAsync(id, token);
            await InvokeAsync(new { Operation = "Remove", Id = id, FullName = before.Value }, token); return;
        }
        if (entry.Kind == "Service") {
            await InvokeAsync(new { Operation = "SetService", Id = id, Mode = state.Value }, token); return;
        }
        var expectedKind = entry.Kind == "Startup" ? "String" : "DWord";
        if (state.Kind != expectedKind) throw new InvalidDataException("Unsupported registry type.");
        using var key = Hive(entry).CreateSubKey(entry.Path, true);
        if (!state.Exists) key.DeleteValue(entry.Name, false);
        else if (entry.Kind == "Startup") key.SetValue(entry.Name, state.Value ?? throw new InvalidDataException(), RegistryValueKind.String);
        else key.SetValue(entry.Name, int.Parse(state.Value ?? throw new InvalidDataException()), RegistryValueKind.DWord);
    }
    public async Task<bool> VerifyRecoveryPointAsync(CancellationToken token) => (await InvokeAsync(new { Operation = "Recovery" }, token)).GetProperty("Verified").GetBoolean();
    private static async Task<JsonElement> InvokeAsync(object request, CancellationToken token)
    {
        using var resource = typeof(WindowsOptimizationBackend).Assembly.GetManifestResourceStream("Clenzy.Windows11.Maintenance.ps1") ?? throw new InvalidOperationException("Maintenance module missing.");
        using var reader = new StreamReader(resource); var script = "$script:ClenzyCatalog = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(MaintenanceCatalog.Json)) + "\")) | ConvertFrom-Json\n" + await reader.ReadToEndAsync(token);
        script += "\ntry { $request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(\"" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))) + "\")) | ConvertFrom-Json; $result = Invoke-ClenzyWindows11 $request; [Console]::Out.Write(($result | ConvertTo-Json -Depth 10 -Compress)) } catch { [Console]::Error.Write($_.Exception.Message); exit 1 }";
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        var bootstrap = "& ([scriptblock]::Create([Console]::In.ReadToEnd()))";
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap)) }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start Windows PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.StandardInput.WriteAsync(script.AsMemory(), timeout.Token); process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) {} throw; }
        var text = await output; var err = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "Windows operation failed." : err);
        if (text.Length > 16 * 1024 * 1024) throw new InvalidDataException("Audit exceeded size limit.");
        using var document = JsonDocument.Parse(text); return document.RootElement.Clone();
    }
}
