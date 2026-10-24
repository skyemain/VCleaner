using CleanSlate.Core.Configuration;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;
using CleanSlate.Core.Rules;
using CleanSlate.Core.Services;

namespace CleanSlate.Cleaners.SystemJunk;

/// <summary>A file-rule module whose rules are produced by a delegate. Used for the simple system junk categories.</summary>
public sealed class SystemFileModule : FileRuleModuleBase
{
    private readonly Func<CleanSlateSettings, bool> _enabled;
    private readonly Func<CleanSlateSettings, bool, RulePlan> _plan;

    public SystemFileModule(
        string id, string name, string description, bool requiresElevation, RiskLevel risk,
        Func<CleanSlateSettings, bool> enabled, Func<CleanSlateSettings, bool, RulePlan> plan,
        ISafeDeletionService deletion, IAppLogger log)
        : base(deletion, log)
    {
        ModuleId = id;
        Name = name;
        Description = description;
        RequiresElevation = requiresElevation;
        RiskLevel = risk;
        _enabled = enabled;
        _plan = plan;
    }

    public override string ModuleId { get; }
    public override string Name { get; }
    public override string Description { get; }
    public override CleanerGroup Group => CleanerGroup.SystemJunk;
    public override RiskLevel RiskLevel { get; }
    public override bool RequiresElevation { get; }

    public override bool IsEnabled(CleanSlateSettings settings) => _enabled(settings);

    protected override RulePlan BuildPlan(CleanSlateSettings settings, bool elevated) => _plan(settings, elevated);
}

/// <summary>Defines the explicit, approved roots for every file-based system junk category.</summary>
public static class SystemModuleFactory
{
    public static readonly TimeSpan TempMinAge = TimeSpan.FromHours(24);
    public static readonly TimeSpan DiagnosticsMinAge = TimeSpan.FromDays(7);
    public static readonly TimeSpan LogMinAge = TimeSpan.FromDays(30);

    public static IReadOnlyList<ICleanerModule> CreateFileModules(SystemPaths paths, ISafeDeletionService deletion, IAppLogger log)
    {
        var local = paths.LocalAppData;
        var win = paths.WindowsDirectory;
        var common = paths.ProgramData;

        return new ICleanerModule[]
        {
            new SystemFileModule(
                "system.user-temp", "User temporary files",
                "Files in your Temp folder that have not been modified for 24 hours and are not in use.",
                false, RiskLevel.Safe,
                s => s.CleanUserTemp,
                (_, _) => UserTempPlan(paths),
                deletion, log),

            new SystemFileModule(
                "system.windows-temp", "Windows temporary files",
                "Files in the Windows Temp folder older than 24 hours. Files you are not permitted to delete are skipped.",
                false, RiskLevel.Low,
                s => s.CleanWindowsTemp,
                (_, _) => Single("windows-temp", "Windows temporary files",
                    "Old files in the Windows Temp folder", paths.WindowsTemp, TempMinAge, RiskLevel.Low),
                deletion, log),

            new SystemFileModule(
                "system.thumbnails", "Thumbnail cache",
                "Explorer thumbnail cache databases. Windows rebuilds them on demand; databases held by Explorer are skipped.",
                false, RiskLevel.Safe,
                s => s.CleanThumbnailCache,
                (_, _) => Single("thumbs", "Thumbnail cache", "Explorer thumbnail cache database",
                    Path.Combine(local, "Microsoft", "Windows", "Explorer"), TimeSpan.Zero, RiskLevel.Safe,
                    patterns: new[] { "thumbcache_*.db" }, recursive: false),
                deletion, log),

            new SystemFileModule(
                "system.shader-cache", "DirectX shader cache",
                "Compiled shader caches that Windows and graphics drivers regenerate automatically.",
                false, RiskLevel.Safe,
                s => s.CleanShaderCache,
                (_, _) => Multi("shader", "DirectX shader cache", "Compiled shader cache file", TimeSpan.Zero, RiskLevel.Safe,
                    Path.Combine(local, "D3DSCache"),
                    Path.Combine(local, "AMD", "DxCache"),
                    Path.Combine(local, "NVIDIA", "DXCache")),
                deletion, log),

            new SystemFileModule(
                "system.wer-user", "Windows Error Reporting (user)",
                "Archived and queued error reports in your profile that are older than 7 days.",
                false, RiskLevel.Safe,
                s => s.CleanErrorReports,
                (_, _) => Multi("wer-user", "Windows error reports", "Old error report file", DiagnosticsMinAge, RiskLevel.Safe,
                    Path.Combine(local, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(local, "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(local, "Microsoft", "Windows", "WER", "Temp")),
                deletion, log),

            new SystemFileModule(
                "system.wer-system", "Windows Error Reporting (system)",
                "System-wide archived and queued error reports older than 7 days. Requires administrator.",
                true, RiskLevel.Low,
                s => s.CleanErrorReports,
                (_, _) => Multi("wer-system", "Windows error reports (system)", "Old system error report file", DiagnosticsMinAge, RiskLevel.Low,
                    Path.Combine(common, "Microsoft", "Windows", "WER", "ReportArchive"),
                    Path.Combine(common, "Microsoft", "Windows", "WER", "ReportQueue"),
                    Path.Combine(common, "Microsoft", "Windows", "WER", "Temp")),
                deletion, log),

            new SystemFileModule(
                "system.crash-dumps-user", "Application crash dumps",
                "Crash dumps (*.dmp) in your profile that are older than 7 days.",
                false, RiskLevel.Low,
                s => s.CleanCrashDumps,
                (_, _) => Single("crash-user", "Crash dumps", "Old application crash dump",
                    Path.Combine(local, "CrashDumps"), DiagnosticsMinAge, RiskLevel.Low,
                    patterns: new[] { "*.dmp", "*.mdmp", "*.hdmp" }, recursive: false),
                deletion, log),

            new SystemFileModule(
                "system.crash-dumps-system", "System diagnostic dumps",
                "Kernel minidumps, live kernel reports and MEMORY.DMP older than 7 days that are not in use. Requires administrator.",
                true, RiskLevel.Low,
                s => s.CleanCrashDumps,
                (_, _) => SystemDumpPlan(win),
                deletion, log),

            new SystemFileModule(
                "system.windows-logs", "Old Windows log files",
                "Rotated servicing and update logs older than 30 days. Active logs, event logs and security/audit logs are never touched. Requires administrator.",
                true, RiskLevel.Low,
                s => s.CleanWindowsLogs,
                (_, _) => WindowsLogPlan(win),
                deletion, log)
        };
    }

    private static RulePlan UserTempPlan(SystemPaths paths)
    {
        var plan = new RulePlan();
        var roots = new[] { paths.UserTemp, paths.UserTmp }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => PathGuard.TryCanonicalize(p, out var canonical, out _) ? canonical : null)
            .Where(p => p is not null)
            .Distinct(PathGuard.Comparer)
            .ToList();

        foreach (var root in roots)
        {
            if (!IsTempLike(root!, paths))
            {
                plan.SkippedCategories.Add(new SkippedCategory("User temporary files",
                    "The TEMP folder is not a recognised temporary location, so it was left untouched.", RiskLevel.Safe));
                continue;
            }

            plan.Rules.Add(new FileCleanRule
            {
                Id = "user-temp:" + root,
                Category = "User temporary files",
                Description = "Temporary file not modified in the last 24 hours",
                RootPath = root!,
                MinAge = TempMinAge,
                Risk = RiskLevel.Safe
            });
        }

        return plan;
    }

    /// <summary>
    /// A relocated TEMP variable is only trusted when it lives inside the user's local application data
    /// or is literally named temp/tmp. This prevents a misconfigured variable from turning into a wipe of a personal folder.
    /// </summary>
    internal static bool IsTempLike(string root, SystemPaths paths)
    {
        if (PathGuard.IsUnder(paths.LocalAppData, root))
        {
            return true;
        }

        var leaf = Path.GetFileName(root);
        return leaf.Equals("temp", StringComparison.OrdinalIgnoreCase) || leaf.Equals("tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static RulePlan Single(
        string id, string category, string description, string root, TimeSpan minAge, RiskLevel risk,
        string[]? patterns = null, bool recursive = true)
    {
        var plan = new RulePlan();
        plan.Rules.Add(new FileCleanRule
        {
            Id = id + ":" + root,
            Category = category,
            Description = description,
            RootPath = root,
            Patterns = patterns ?? new[] { "*" },
            Recursive = recursive,
            MinAge = minAge,
            Risk = risk
        });
        return plan;
    }

    private static RulePlan Multi(string id, string category, string description, TimeSpan minAge, RiskLevel risk, params string[] roots)
    {
        var plan = new RulePlan();
        foreach (var root in roots)
        {
            plan.Rules.Add(new FileCleanRule
            {
                Id = id + ":" + root,
                Category = category,
                Description = description,
                RootPath = root,
                MinAge = minAge,
                Risk = risk
            });
        }

        return plan;
    }

    private static RulePlan SystemDumpPlan(string win)
    {
        var plan = new RulePlan();
        plan.Rules.Add(new FileCleanRule
        {
            Id = "dump-mini:" + win,
            Category = "System diagnostic dumps",
            Description = "Old kernel minidump",
            RootPath = Path.Combine(win, "Minidump"),
            Patterns = new[] { "*.dmp" },
            Recursive = false,
            MinAge = DiagnosticsMinAge,
            Risk = RiskLevel.Low
        });
        plan.Rules.Add(new FileCleanRule
        {
            Id = "dump-live:" + win,
            Category = "System diagnostic dumps",
            Description = "Old live kernel report",
            RootPath = Path.Combine(win, "LiveKernelReports"),
            Patterns = new[] { "*.dmp" },
            Recursive = true,
            MinAge = DiagnosticsMinAge,
            Risk = RiskLevel.Low
        });
        plan.Rules.Add(new FileCleanRule
        {
            Id = "dump-memory:" + win,
            Category = "System diagnostic dumps",
            Description = "Old full memory dump",
            RootPath = win,
            Patterns = new[] { "MEMORY.DMP" },
            Recursive = false,
            MinAge = DiagnosticsMinAge,
            Risk = RiskLevel.Low,
            AllowLiteralFilesInContainer = true
        });
        return plan;
    }

    private static RulePlan WindowsLogPlan(string win)
    {
        var plan = new RulePlan();
        plan.Rules.Add(new FileCleanRule
        {
            Id = "log-cbs:" + win,
            Category = "Old Windows logs",
            Description = "Rotated servicing log",
            RootPath = Path.Combine(win, "Logs", "CBS"),
            Patterns = new[] { "CbsPersist_*.log", "CbsPersist_*.cab" },
            Recursive = false,
            MinAge = LogMinAge,
            Risk = RiskLevel.Low
        });
        plan.Rules.Add(new FileCleanRule
        {
            Id = "log-dism:" + win,
            Category = "Old Windows logs",
            Description = "Old DISM log",
            RootPath = Path.Combine(win, "Logs", "DISM"),
            Patterns = new[] { "*.log" },
            Recursive = false,
            MinAge = LogMinAge,
            Risk = RiskLevel.Low
        });
        plan.Rules.Add(new FileCleanRule
        {
            Id = "log-wu:" + win,
            Category = "Old Windows logs",
            Description = "Old Windows Update trace log",
            RootPath = Path.Combine(win, "Logs", "WindowsUpdate"),
            Patterns = new[] { "*.etl" },
            Recursive = false,
            MinAge = LogMinAge,
            Risk = RiskLevel.Low
        });
        return plan;
    }
}
