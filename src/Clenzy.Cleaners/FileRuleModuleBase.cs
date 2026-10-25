using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Rules;
using Clenzy.Core.Services;

namespace Clenzy.Cleaners;

/// <summary>The rules a file module wants to run, plus categories that were skipped before any scanning.</summary>
public sealed class RulePlan
{
    public List<FileCleanRule> Rules { get; } = new();
    public List<SkippedCategory> SkippedCategories { get; } = new();

    /// <summary>When set and no rules exist, the whole module reports this status instead of "completed".</summary>
    public ModuleScanStatus? EmptyStatus { get; set; }

    public string? Message { get; set; }
}

public sealed record SkippedCategory(string Category, string Reason, RiskLevel Risk);

/// <summary>
/// Shared scan/clean implementation for modules that delete files under explicit roots.
/// Preview (scan) and execution (clean) use the same <see cref="ISafeDeletionService"/> eligibility logic.
/// </summary>
public abstract class FileRuleModuleBase : ICleanerModule
{
    private const int MaxSamples = 50;
    private const int MaxOutcomes = 500;
    private const int ScanBatch = 256;
    private static long _nextFindingId;

    protected FileRuleModuleBase(ISafeDeletionService deletion, IAppLogger log)
    {
        Deletion = deletion;
        Log = log;
    }

    protected ISafeDeletionService Deletion { get; }
    protected IAppLogger Log { get; }

    public abstract string ModuleId { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract CleanerGroup Group { get; }
    public virtual RiskLevel RiskLevel => RiskLevel.Safe;
    public virtual bool RequiresElevation => false;
    public bool SupportsPreview => true;

    public abstract bool IsEnabled(ClenzySettings settings);

    protected abstract RulePlan BuildPlan(ClenzySettings settings, bool elevated);

    public Task<ModuleScanResult> ScanAsync(ScanContext context, CancellationToken cancellationToken) =>
        Task.Run(() => Scan(context, cancellationToken), cancellationToken);

    private ModuleScanResult Scan(ScanContext context, CancellationToken ct)
    {
        var plan = BuildPlan(context.Settings, context.IsElevated);
        var findings = new List<Finding>();
        long scanned = 0;
        long pendingItems = 0;
        long pendingBytes = 0;
        var skippedCount = 0;
        var samples = new List<SkippedSample>();
        var truncated = false;
        var errors = new List<string>();

        foreach (var skipped in plan.SkippedCategories)
        {
            findings.Add(MakeInformational(skipped.Category, skipped.Reason, skipped.Risk));
        }

        if (plan.Rules.Count == 0 && plan.EmptyStatus is { } emptyStatus && findings.Count == 0)
        {
            return ModuleScanResult.Empty(ModuleId, Name, Group, emptyStatus, plan.Message);
        }

        foreach (var rule in plan.Rules)
        {
            ct.ThrowIfCancellationRequested();
            var root = Deletion.Prepare(rule, context.Exclusions, out var reason);
            if (root is null)
            {
                if (reason is not null && !reason.StartsWith("Not present", StringComparison.Ordinal))
                {
                    findings.Add(MakeInformational(rule.Category, reason, rule.Risk, rule.RootPath));
                }

                continue;
            }

            context.Reporter.Operation($"Scanning {rule.Category}...");

            try
            {
                foreach (var entry in Deletion.Enumerate(root, context.NowUtc, ct))
                {
                    scanned++;
                    pendingItems++;

                    switch (entry.Eligibility.Kind)
                    {
                        case EligibilityKind.Eligible:
                            if (!context.Claims.TryClaim(entry.Path))
                            {
                                break;
                            }

                            if (findings.Count >= context.MaxFindingsPerModule)
                            {
                                truncated = true;
                                break;
                            }

                            pendingBytes += entry.Length;
                            findings.Add(new Finding
                            {
                                Id = Interlocked.Increment(ref _nextFindingId),
                                ModuleId = ModuleId,
                                Group = Group,
                                Category = rule.Category,
                                Location = entry.Path,
                                Description = rule.Description,
                                EstimatedBytes = entry.Length,
                                Risk = rule.Risk,
                                Action = ActionType.DeleteFile,
                                IsSelected = true,
                                IsEligible = true,
                                AutoCleanAllowed = rule.AutoCleanAllowed,
                                ConfirmationRequired = rule.ConfirmationRequired,
                                IsPermanent = true,
                                RuleId = rule.Id,
                                LastWriteUtcTicks = entry.LastWriteUtc.Ticks
                            });
                            break;

                        case EligibilityKind.Skipped:
                            skippedCount++;
                            if (samples.Count < MaxSamples)
                            {
                                samples.Add(new SkippedSample(entry.Path, entry.Eligibility.Reason ?? "Skipped"));
                            }

                            break;
                    }

                    if (pendingItems >= ScanBatch)
                    {
                        context.Reporter.Scanned(pendingItems, pendingBytes);
                        pendingItems = 0;
                        pendingBytes = 0;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{rule.Category}: {ex.Message}");
            }
        }

        context.Reporter.Scanned(pendingItems, pendingBytes);

        return new ModuleScanResult
        {
            ModuleId = ModuleId,
            ModuleName = Name,
            Group = Group,
            Status = ModuleScanStatus.Completed,
            Message = plan.Message,
            Findings = findings,
            ItemsScanned = scanned,
            SkippedItemCount = skippedCount,
            SkippedSamples = samples,
            Errors = errors,
            FindingsTruncated = truncated
        };
    }

    private Finding MakeInformational(string category, string reason, RiskLevel risk, string? location = null) => new()
    {
        Id = Interlocked.Increment(ref _nextFindingId),
        ModuleId = ModuleId,
        Group = Group,
        Category = category,
        Location = location ?? category,
        Description = reason,
        Risk = risk,
        Action = ActionType.None,
        IsSelected = false,
        IsEligible = false,
        SkipReason = reason,
        IsPermanent = false
    };

    public Task<ValidationResult> ValidateAsync(Finding finding, CleanContext context, CancellationToken cancellationToken)
    {
        if (finding.ModuleId != ModuleId || finding.Action != ActionType.DeleteFile || finding.RuleId is null)
        {
            return Task.FromResult(ValidationResult.Invalid("Finding does not belong to this module."));
        }

        var roots = PrepareRoots(context.Settings, context.IsElevated, context.Exclusions);
        return Task.FromResult(ValidateAgainst(roots, finding));
    }

    private static ValidationResult ValidateAgainst(Dictionary<string, PreparedRoot> roots, Finding finding)
    {
        if (finding.RuleId is null || !roots.TryGetValue(finding.RuleId, out var root))
        {
            return ValidationResult.Invalid("Cleaning rule is no longer enabled or its folder is unavailable.");
        }

        if (!PathGuard.TryCanonicalize(finding.Location, out var canonical, out var error))
        {
            return ValidationResult.Invalid(error ?? "Invalid path.");
        }

        return PathGuard.IsUnder(root.Path, canonical)
            ? ValidationResult.Valid
            : ValidationResult.Invalid("Outside the approved folder.");
    }

    private Dictionary<string, PreparedRoot> PrepareRoots(ClenzySettings settings, bool elevated, ExclusionPolicy exclusions)
    {
        var map = new Dictionary<string, PreparedRoot>(StringComparer.Ordinal);
        foreach (var rule in BuildPlan(settings, elevated).Rules)
        {
            var root = Deletion.Prepare(rule, exclusions, out _);
            if (root is not null)
            {
                map[rule.Id] = root;
            }
        }

        return map;
    }

    public Task<ModuleCleanResult> CleanAsync(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken) =>
        Task.Run(() => Clean(context, findings, cancellationToken), CancellationToken.None);

    /// <summary>Called once before the findings are executed, for example to close an application that holds the files open.</summary>
    protected virtual void BeforeClean(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
    }

    private ModuleCleanResult Clean(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken ct)
    {
        BeforeClean(context, findings, ct);
        var roots = PrepareRoots(context.Settings, context.IsElevated, context.Exclusions);
        var outcomes = new List<ActionOutcome>();
        var perCategory = new Dictionary<string, CategoryAccumulator>(StringComparer.Ordinal);
        var parents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var canceled = false;
        var cleaned = 0;
        var skipped = 0;
        var errors = 0;
        long recovered = 0;

        foreach (var finding in findings)
        {
            if (ct.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            if (!perCategory.TryGetValue(finding.Category, out var acc))
            {
                acc = new CategoryAccumulator();
                perCategory[finding.Category] = acc;
            }

            acc.Estimated += finding.EstimatedBytes;

            DeletionOutcome outcome;
            var validation = ValidateAgainst(roots, finding);
            if (finding.ModuleId != ModuleId || finding.Action != ActionType.DeleteFile)
            {
                outcome = DeletionOutcome.Skipped("Not a file-cleaning action for this module");
            }
            else if (!validation.IsValid)
            {
                outcome = DeletionOutcome.Skipped(validation.Reason ?? "Invalid");
            }
            else
            {
                var root = roots[finding.RuleId!];
                outcome = Deletion.DeleteFile(root, finding.Location, finding.LastWriteUtcTicks, finding.EstimatedBytes, context.NowUtc);
                if (outcome.Kind == OutcomeKind.Cleaned)
                {
                    var parent = Path.GetDirectoryName(finding.Location);
                    if (parent is not null)
                    {
                        if (!parents.TryGetValue(finding.RuleId!, out var set))
                        {
                            set = new HashSet<string>(PathGuard.Comparer);
                            parents[finding.RuleId!] = set;
                        }

                        set.Add(parent);
                    }
                }
            }

            switch (outcome.Kind)
            {
                case OutcomeKind.Cleaned:
                    cleaned++;
                    acc.Cleaned++;
                    acc.Recovered += outcome.Bytes;
                    recovered += outcome.Bytes;
                    Log.Audit($"DELETED {finding.Location} ({outcome.Bytes} bytes) [{ModuleId}/{finding.Category}]");
                    context.Reporter.Processed(1, outcome.Bytes, 0, 0);
                    break;

                case OutcomeKind.Skipped:
                    skipped++;
                    acc.Skipped++;
                    AddOutcome(outcomes, new ActionOutcome(OutcomeKind.Skipped, ModuleId, finding.Category, finding.Location, outcome.Message, 0));
                    Log.Audit($"SKIPPED {finding.Location}: {outcome.Message} [{ModuleId}/{finding.Category}]");
                    context.Reporter.Processed(1, 0, 1, 0);
                    break;

                default:
                    errors++;
                    acc.Errors++;
                    AddOutcome(outcomes, new ActionOutcome(OutcomeKind.Error, ModuleId, finding.Category, finding.Location, outcome.Message, 0));
                    Log.Audit($"ERROR {finding.Location}: {outcome.Message} [{ModuleId}/{finding.Category}]");
                    context.Reporter.Processed(1, 0, 0, 1);
                    break;
            }
        }

        var removedDirectories = 0;
        foreach (var (ruleId, directories) in parents)
        {
            if (roots.TryGetValue(ruleId, out var root))
            {
                removedDirectories += Deletion.PruneEmptyDirectories(root, directories, CancellationToken.None);
            }
        }

        return new ModuleCleanResult
        {
            ModuleId = ModuleId,
            ModuleName = Name,
            Group = Group,
            ItemsCleaned = cleaned,
            ItemsSkipped = skipped,
            Errors = errors,
            BytesRecovered = recovered,
            WasCanceled = canceled,
            DirectoriesRemoved = removedDirectories,
            Outcomes = outcomes,
            Categories = perCategory
                .Select(kv => new CategoryResult(ModuleId, Group, kv.Key, kv.Value.Cleaned, kv.Value.Skipped, kv.Value.Errors, kv.Value.Estimated, kv.Value.Recovered))
                .ToList()
        };
    }

    private static void AddOutcome(List<ActionOutcome> outcomes, ActionOutcome outcome)
    {
        if (outcomes.Count < MaxOutcomes)
        {
            outcomes.Add(outcome);
        }
    }

    private sealed class CategoryAccumulator
    {
        public int Cleaned;
        public int Skipped;
        public int Errors;
        public long Estimated;
        public long Recovered;
    }
}
