using Clenzy.Cleaners;
using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Rules;
using Clenzy.Core.Services;

namespace Clenzy.Cleaners.Privacy;

/// <summary>
/// Optional Windows activity-history cleanup. Every target is a fixed, scoped HKCU key (values only are removed,
/// never the key) or the shortcut files in the Recent folder. Pinned items, jump lists and the files that the
/// shortcuts point to are never touched. Disabled by default and always confirmed in the review screen.
/// </summary>
public sealed class PrivacyModule : ICleanerModule
{
    public const string RunHistory = "Run dialog history";
    public const string AddressHistory = "Explorer address bar history";
    public const string RecentItems = "Recent items";

    private sealed record Target(string Id, string Category, RegistryKeyRef Key, bool IncludeSubKeys, string Explanation);

    private static readonly RegistryKeyRef ExplorerKey = new(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Explorer");

    private static readonly Target[] Targets =
    {
        new("runmru", RunHistory, ExplorerKey.Child("RunMRU"), false,
            "the commands remembered by the Run dialog (Win+R)"),
        new("typedpaths", AddressHistory, ExplorerKey.Child("TypedPaths"), false,
            "the folder paths typed into the File Explorer address bar"),
        new("recentdocs", RecentItems, ExplorerKey.Child("RecentDocs"), true,
            "the list of recently opened documents that File Explorer remembers")
    };

    private static long _nextId = 4_000_000_000;

    private readonly IRegistryAccess _registry;
    private readonly SystemPaths _paths;
    private readonly IAppLogger _log;
    private readonly RecentFilesModule _recentFiles;

    public PrivacyModule(IRegistryAccess registry, SystemPaths paths, ISafeDeletionService deletion, IAppLogger log)
    {
        _registry = registry;
        _paths = paths;
        _log = log;
        _recentFiles = new RecentFilesModule(paths, deletion, log);
    }

    public string ModuleId => "privacy.activity";
    public string Name => "Windows activity history";
    public string Description => "Optional. Clears Run dialog history, File Explorer address bar history and recent-item references. Pinned and favorite items are kept.";
    public CleanerGroup Group => CleanerGroup.Privacy;
    public RiskLevel RiskLevel => RiskLevel.Low;
    public bool RequiresElevation => false;
    public bool SupportsPreview => true;

    public bool IsEnabled(ClenzySettings settings) => settings.ClearActivityHistory;

    private static bool TargetEnabled(ClenzySettings s, string category) => category switch
    {
        RunHistory => s.ClearRunHistory,
        AddressHistory => s.ClearExplorerAddressHistory,
        RecentItems => s.ClearRecentItems,
        _ => false
    };

    public async Task<ModuleScanResult> ScanAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = await _recentFiles.ScanAsync(context, cancellationToken).ConfigureAwait(false);
        var findings = new List<Finding>(result.Findings);
        var scanned = result.ItemsScanned;

        foreach (var target in Targets.Where(t => TargetEnabled(context.Settings, t.Category)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = CountValues(target);
            scanned += count;
            if (count == 0)
            {
                continue;
            }

            findings.Add(new Finding
            {
                Id = Interlocked.Increment(ref _nextId),
                ModuleId = ModuleId,
                Group = Group,
                Category = target.Category,
                Location = target.Key.DisplayPath,
                Description = $"Remove {count} entr{(count == 1 ? "y" : "ies")}: {target.Explanation}",
                Risk = RiskLevel.Low,
                Action = ActionType.ClearHistoryValues,
                IsSelected = true,
                IsEligible = true,
                AutoCleanAllowed = false,
                ConfirmationRequired = true,
                IsPermanent = true,
                RuleId = target.Id
            });
        }

        context.Reporter.Scanned(scanned - result.ItemsScanned, 0);

        return new ModuleScanResult
        {
            ModuleId = ModuleId,
            ModuleName = Name,
            Group = Group,
            Status = result.Status,
            Message = result.Message,
            Findings = findings,
            ItemsScanned = scanned,
            SkippedItemCount = result.SkippedItemCount,
            SkippedSamples = result.SkippedSamples,
            Errors = result.Errors
        };
    }

    private int CountValues(Target target)
    {
        try
        {
            var count = _registry.GetValueNames(target.Key).Count;
            if (target.IncludeSubKeys)
            {
                count += _registry.GetSubKeyNames(target.Key).Sum(sub => _registry.GetValueNames(target.Key.Child(sub)).Count);
            }

            return count;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return 0;
        }
    }

    public Task<ValidationResult> ValidateAsync(Finding finding, CleanContext context, CancellationToken cancellationToken)
    {
        if (finding.ModuleId != ModuleId)
        {
            return Task.FromResult(ValidationResult.Invalid("Finding does not belong to this module."));
        }

        if (finding.Action == ActionType.DeleteFile)
        {
            return _recentFiles.ValidateAsync(finding, context, cancellationToken);
        }

        var target = Targets.FirstOrDefault(t => t.Id == finding.RuleId);
        if (finding.Action != ActionType.ClearHistoryValues || target is null)
        {
            return Task.FromResult(ValidationResult.Invalid("Unsupported privacy target."));
        }

        return Task.FromResult(context.Settings.ClearActivityHistory && TargetEnabled(context.Settings, target.Category)
            ? ValidationResult.Valid
            : ValidationResult.Invalid("This privacy category is not enabled."));
    }

    public async Task<ModuleCleanResult> CleanAsync(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        var fileFindings = findings.Where(f => f.Action == ActionType.DeleteFile).ToList();
        var fileResult = fileFindings.Count > 0
            ? await _recentFiles.CleanAsync(context, fileFindings, cancellationToken).ConfigureAwait(false)
            : new ModuleCleanResult { ModuleId = ModuleId, ModuleName = Name, Group = Group };

        var categories = fileResult.Categories.Select(c => c with { ModuleId = ModuleId }).ToList();
        var outcomes = fileResult.Outcomes.Select(o => o with { ModuleId = ModuleId }).ToList();
        var cleaned = fileResult.ItemsCleaned;
        var skipped = fileResult.ItemsSkipped;
        var errors = fileResult.Errors;
        var canceled = fileResult.WasCanceled;

        foreach (var finding in findings.Where(f => f.Action == ActionType.ClearHistoryValues))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var validation = await ValidateAsync(finding, context, cancellationToken).ConfigureAwait(false);
            var target = Targets.FirstOrDefault(t => t.Id == finding.RuleId);
            if (!validation.IsValid || target is null)
            {
                skipped++;
                outcomes.Add(new ActionOutcome(OutcomeKind.Skipped, ModuleId, finding.Category, finding.Location, validation.Reason, 0));
                context.Reporter.Processed(1, 0, 1, 0);
                continue;
            }

            var (removed, failed) = ClearTarget(target);
            cleaned += removed;
            errors += failed;
            context.Reporter.Processed(1, 0, 0, failed > 0 ? 1 : 0);
            categories.Add(new CategoryResult(ModuleId, Group, target.Category, removed, 0, failed, 0, 0));
            _log.Audit($"HISTORY CLEARED {target.Key.DisplayPath}: {removed} value(s) removed, {failed} failed");
            if (failed > 0)
            {
                outcomes.Add(new ActionOutcome(OutcomeKind.Error, ModuleId, target.Category, target.Key.DisplayPath, $"{failed} value(s) could not be removed.", 0));
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
            WasCanceled = canceled,
            Categories = categories,
            Outcomes = outcomes
        };
    }

    private (int Removed, int Failed) ClearTarget(Target target)
    {
        var removed = 0;
        var failed = 0;
        var keys = new List<RegistryKeyRef> { target.Key };
        try
        {
            if (target.IncludeSubKeys)
            {
                keys.AddRange(_registry.GetSubKeyNames(target.Key).Select(target.Key.Child));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            failed++;
        }

        foreach (var key in keys)
        {
            IReadOnlyList<string> names;
            try
            {
                names = _registry.GetValueNames(key);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                failed++;
                continue;
            }

            foreach (var name in names)
            {
                try
                {
                    _registry.DeleteValue(key, name);
                    removed++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    failed++;
                }
            }
        }

        return (removed, failed);
    }

    /// <summary>Shortcut files directly inside the Recent folder (not jump lists, not sub-folders).</summary>
    private sealed class RecentFilesModule : FileRuleModuleBase
    {
        private readonly SystemPaths _paths;

        public RecentFilesModule(SystemPaths paths, ISafeDeletionService deletion, IAppLogger log) : base(deletion, log)
        {
            _paths = paths;
        }

        public override string ModuleId => "privacy.activity";
        public override string Name => "Windows activity history";
        public override string Description => "Recent item shortcuts";
        public override CleanerGroup Group => CleanerGroup.Privacy;
        public override bool IsEnabled(ClenzySettings settings) => settings.ClearActivityHistory && settings.ClearRecentItems;

        protected override RulePlan BuildPlan(ClenzySettings settings, bool elevated)
        {
            var plan = new RulePlan();
            if (!IsEnabled(settings))
            {
                return plan;
            }

            plan.Rules.Add(new FileCleanRule
            {
                Id = "recent-lnk",
                Category = RecentItems,
                Description = "Recent item shortcut (the file it points to is not touched)",
                RootPath = Path.Combine(_paths.RoamingAppData, "Microsoft", "Windows", "Recent"),
                Patterns = new[] { "*.lnk" },
                Recursive = false,
                Risk = RiskLevel.Low,
                AutoCleanAllowed = false,
                ConfirmationRequired = true,
                PruneEmptyDirectories = false
            });
            return plan;
        }
    }
}
