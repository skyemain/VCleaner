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
    private static readonly Dictionary<string, (string Path, string Name, int Desired)> Settings = new() {
        ["advertising"] = (@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
        ["extensions"] = (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 0),
        ["hidden"] = (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 1),
        ["explorerads"] = (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSyncProviderNotifications", 0),
        ["taskview"] = (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowTaskViewButton", 0),
        ["alignment"] = (@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 0),
        ["theme"] = (@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0)
    };
    public async Task<OptimizationAudit> AuditAsync(CancellationToken token)
    {
        if (!Supported) return new("Windows 11 is required for live changes.", false, false, Array.Empty<OptimizationFinding>(), "No live scan was run.");
        var audit = await InvokeAsync(new { Operation = "Audit" }, token);
        var findings = new List<OptimizationFinding>();
        foreach (var action in OptimizationCatalog.Actions) {
            token.ThrowIfCancellationRequested();
            if (action.Irreversible) {
                var p = await InvokeAsync(new { Operation = "Package", Id = action.Id }, token);
                var exists = p.GetProperty("Exists").GetBoolean();
                var state = new OptimizationState(exists, exists ? p.GetProperty("FullName").GetString() : null, "Package");
                findings.Add(new(action, exists ? "Installed" : "Not installed", exists && !p.GetProperty("Blocked").GetBoolean(), p.GetProperty("Reason").GetString(), state));
            } else {
                var state = await ReadAsync(action.Id, token); var desired = await DesiredAsync(action.Id, token);
                findings.Add(new(action, state.Exists ? state.Value ?? "Unknown" : "Not configured", state.Exists && state.Kind == "DWord" && state != desired,
                    !state.Exists ? "This build/profile has no existing supported setting; use Windows Settings." : state == desired ? "Already set" : null, state));
            }
        }
        var os = audit.GetProperty("OS");
        return new($"{os.GetProperty("Caption").GetString()} · build {os.GetProperty("BuildNumber").GetString()} · {os.GetProperty("OSArchitecture").GetString()}", true,
            audit.GetProperty("Administrator").GetBoolean(), findings, JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true }));
    }
    public async Task<OptimizationState> ReadAsync(string id, CancellationToken token)
    {
        var action = OptimizationCatalog.Get(id); token.ThrowIfCancellationRequested();
        if (action.Irreversible) {
            var p = await InvokeAsync(new { Operation = "Package", Id = id }, token); var exists = p.GetProperty("Exists").GetBoolean();
            return new(exists, exists ? p.GetProperty("FullName").GetString() : null, "Package");
        }
        var s = Settings[id]; using var key = Registry.CurrentUser.OpenSubKey(s.Path);
        if (key is null || !key.GetValueNames().Contains(s.Name, StringComparer.OrdinalIgnoreCase)) return new(false, null, "DWord");
        var kind = key.GetValueKind(s.Name);
        return new(true, key.GetValue(s.Name)?.ToString(), kind.ToString());
    }
    public Task<OptimizationState> DesiredAsync(string id, CancellationToken token) => Task.FromResult(OptimizationCatalog.Get(id).Irreversible ? new OptimizationState(false, null, "Package") : new OptimizationState(true, Settings[id].Desired.ToString(), "DWord"));
    public async Task WriteAsync(string id, OptimizationState state, CancellationToken token)
    {
        if (!Supported) throw new PlatformNotSupportedException();
        var action = OptimizationCatalog.Get(id); token.ThrowIfCancellationRequested();
        if (action.Irreversible) {
            if (state.Exists) throw new InvalidOperationException("Reinstall this app from Microsoft Store; original app data cannot be restored automatically.");
            var before = await ReadAsync(id, token);
            await InvokeAsync(new { Operation = "Remove", Id = id, FullName = before.Value }, token); return;
        }
        if (state.Kind != "DWord") throw new InvalidDataException("Unsupported registry type.");
        var s = Settings[id]; using var key = Registry.CurrentUser.CreateSubKey(s.Path, true);
        if (state.Exists) key.SetValue(s.Name, int.Parse(state.Value ?? throw new InvalidDataException()), RegistryValueKind.DWord);
        else key.DeleteValue(s.Name, false);
    }
    public async Task<bool> VerifyRecoveryPointAsync(CancellationToken token) => (await InvokeAsync(new { Operation = "Recovery" }, token)).GetProperty("Verified").GetBoolean();
    private static async Task<JsonElement> InvokeAsync(object request, CancellationToken token)
    {
        using var resource = typeof(WindowsOptimizationBackend).Assembly.GetManifestResourceStream("Clenzy.Windows11.Maintenance.ps1") ?? throw new InvalidOperationException("Maintenance module missing.");
        using var reader = new StreamReader(resource); var script = await reader.ReadToEndAsync(token);
        script += "\ntry { $request = [Console]::In.ReadToEnd() | ConvertFrom-Json; $result = Invoke-ClenzyWindows11 $request; [Console]::Out.Write(($result | ConvertTo-Json -Depth 10 -Compress)) } catch { [Console]::Error.Write($_.Exception.Message); exit 1 }";
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start Windows PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request)); process.StandardInput.Close();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) {} throw; }
        var text = await output; var err = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "Windows operation failed." : err);
        if (text.Length > 16 * 1024 * 1024) throw new InvalidDataException("Audit exceeded size limit.");
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
