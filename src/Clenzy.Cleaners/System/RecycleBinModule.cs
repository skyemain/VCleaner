using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;

namespace Clenzy.Cleaners.SystemJunk;

/// <summary>Empties the Recycle Bin through the shell API. Permanent, opt-in and always confirmed.</summary>
public sealed class RecycleBinModule : ICleanerModule
{
    private const string CategoryName = "Recycle Bin";
    private static long _nextId = 1_000_000_000;
    private readonly IRecycleBinService _bin;
    private readonly IAppLogger _log;

    public RecycleBinModule(IRecycleBinService bin, IAppLogger log)
    {
        _bin = bin;
        _log = log;
    }

    public string ModuleId => "system.recycle-bin";
    public string Name => "Recycle Bin";
    public string Description => "Permanently deletes everything in the Recycle Bin. This cannot be undone.";
    public CleanerGroup Group => CleanerGroup.SystemJunk;
    public RiskLevel RiskLevel => RiskLevel.High;
    public bool RequiresElevation => false;
    public bool SupportsPreview => true;

    public bool IsEnabled(ClenzySettings settings) => settings.EmptyRecycleBin;

    public Task<ModuleScanResult> ScanAsync(ScanContext context, CancellationToken cancellationToken)
    {
        if (!_bin.IsSupported)
        {
            return Task.FromResult(ModuleScanResult.Empty(ModuleId, Name, Group, ModuleScanStatus.Unavailable, "Recycle Bin is not available on this system."));
        }

        RecycleBinInfo info;
        try
        {
            info = _bin.Query();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Warning($"Recycle Bin query failed: {ex.Message}");
            return Task.FromResult(ModuleScanResult.Empty(ModuleId, Name, Group, ModuleScanStatus.Failed, "Could not read the Recycle Bin size."));
        }

        context.Reporter.Scanned(info.Items, info.Bytes);
        var result = new ModuleScanResult
        {
            ModuleId = ModuleId,
            ModuleName = Name,
            Group = Group,
            ItemsScanned = info.Items,
            Message = info.Items == 0 ? "Recycle Bin is empty" : null
        };

        if (info.Items > 0)
        {
            result.Findings.Add(new Finding
            {
                Id = Interlocked.Increment(ref _nextId),
                ModuleId = ModuleId,
                Group = Group,
                Category = CategoryName,
                Location = "Recycle Bin (all drives)",
                Description = $"Permanently delete {info.Items:N0} item(s) in the Recycle Bin",
                EstimatedBytes = info.Bytes,
                Risk = RiskLevel.High,
                Action = ActionType.EmptyRecycleBin,
                IsSelected = true,
                IsEligible = true,
                AutoCleanAllowed = false,
                ConfirmationRequired = true,
                IsPermanent = true
            });
        }

        return Task.FromResult(result);
    }

    public Task<ValidationResult> ValidateAsync(Finding finding, CleanContext context, CancellationToken cancellationToken) =>
        Task.FromResult(finding.Action == ActionType.EmptyRecycleBin && finding.ModuleId == ModuleId && context.Settings.EmptyRecycleBin
            ? ValidationResult.Valid
            : ValidationResult.Invalid("Emptying the Recycle Bin is not enabled."));

    public async Task<ModuleCleanResult> CleanAsync(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        var finding = findings.FirstOrDefault(f => f.Action == ActionType.EmptyRecycleBin);
        if (finding is null)
        {
            return new ModuleCleanResult { ModuleId = ModuleId, ModuleName = Name, Group = Group };
        }

        var validation = await ValidateAsync(finding, context, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            context.Reporter.Processed(1, 0, 1, 0);
            return Result(0, 1, 0, 0, validation.Reason, finding);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var before = _bin.Query();
            _log.Audit($"RECYCLE BIN EMPTY requested ({before.Items} items, {before.Bytes} bytes)");
            await Task.Run(_bin.Empty, CancellationToken.None).ConfigureAwait(false);
            var after = _bin.Query();
            var cleaned = (int)Math.Max(0, before.Items - after.Items);
            var recovered = Math.Max(0, before.Bytes - after.Bytes);

            context.Reporter.Processed(1, recovered, 0, 0);
            if (after.Items > 0)
            {
                return Result(cleaned, 1, 0, recovered, "Some items could not be removed", finding);
            }

            return Result(cleaned, 0, 0, recovered, null, finding);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Emptying the Recycle Bin failed.", ex);
            context.Reporter.Processed(1, 0, 0, 1);
            return Result(0, 0, 1, 0, "Could not empty the Recycle Bin: " + ex.Message, finding);
        }
    }

    private ModuleCleanResult Result(int cleaned, int skipped, int errors, long recovered, string? message, Finding finding) => new()
    {
        ModuleId = ModuleId,
        ModuleName = Name,
        Group = Group,
        ItemsCleaned = cleaned,
        ItemsSkipped = skipped,
        Errors = errors,
        BytesRecovered = recovered,
        Message = message,
        Categories = new[] { new CategoryResult(ModuleId, Group, CategoryName, cleaned, skipped, errors, finding.EstimatedBytes, recovered) },
        Outcomes = message is null
            ? Array.Empty<ActionOutcome>()
            : new[] { new ActionOutcome(errors > 0 ? OutcomeKind.Error : OutcomeKind.Skipped, ModuleId, CategoryName, finding.Location, message, 0) }
    };
}
