using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;

namespace Clenzy.Cleaners.Registry;

/// <summary>
/// Registry diagnostics module. Scanning is strictly read-only. This module never repairs anything on its own:
/// repairs run only through <see cref="RegistryRepairService"/> after explicit review and backup.
/// </summary>
public sealed class RegistryModule : ICleanerModule
{
    private static long _nextId = 3_000_000_000;
    private readonly RegistryScanner _scanner;
    private readonly IAppLogger _log;

    public RegistryModule(RegistryScanner scanner, IAppLogger log)
    {
        _scanner = scanner;
        _log = log;
    }

    public string ModuleId => "registry.diagnostics";
    public string Name => "Registry diagnostics";
    public string Description => "Finds startup entries, App Paths registrations and uninstall entries whose program is verifiably missing. Read-only.";
    public CleanerGroup Group => CleanerGroup.Registry;
    public RiskLevel RiskLevel => RiskLevel.Medium;
    public bool RequiresElevation => false;
    public bool SupportsPreview => true;

    public bool IsEnabled(ClenzySettings settings) => settings.ScanRegistry;

    public Task<ModuleScanResult> ScanAsync(ScanContext context, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            context.Reporter.Operation("Scanning the registry (read-only)...");
            var scan = _scanner.Scan(cancellationToken);
            context.Reporter.Scanned(scan.EntriesExamined, 0);

            var result = new ModuleScanResult
            {
                ModuleId = ModuleId,
                ModuleName = Name,
                Group = Group,
                ItemsScanned = scan.EntriesExamined,
                Errors = scan.Errors.ToList()
            };

            foreach (var issue in scan.Issues)
            {
                var needsAdmin = issue.RequiresElevation && !context.IsElevated;
                result.Findings.Add(new Finding
                {
                    Id = Interlocked.Increment(ref _nextId),
                    ModuleId = ModuleId,
                    Group = Group,
                    Category = issue.RuleTitle,
                    Location = issue.DisplayLocation,
                    Description = issue.Reason,
                    EstimatedBytes = 0,
                    Risk = issue.Risk,
                    Action = ActionType.RepairRegistryEntry,
                    IsSelected = false,
                    IsEligible = !needsAdmin,
                    SkipReason = needsAdmin ? "Repairing this entry requires administrator rights." : null,
                    AutoCleanAllowed = false,
                    ConfirmationRequired = true,
                    IsPermanent = false,
                    Payload = issue
                });
            }

            _log.Info($"Registry diagnostics: {scan.Issues.Count} issue(s) detected in {scan.EntriesExamined} entries.");
            return result;
        }, cancellationToken);

    public Task<ValidationResult> ValidateAsync(Finding finding, CleanContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ValidationResult.Invalid("Registry repairs run only from the registry review screen."));

    public Task<ModuleCleanResult> CleanAsync(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken) =>
        Task.FromResult(new ModuleCleanResult
        {
            ModuleId = ModuleId,
            ModuleName = Name,
            Group = Group,
            ItemsSkipped = findings.Count,
            Message = "Registry repairs are never part of automatic cleaning."
        });
}
