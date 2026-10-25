using Clenzy.Core.Models;
using Clenzy.Core.Services;

namespace Clenzy.Presentation.ViewModels;

public sealed record DetailRow(string Group, string Module, string Category, string Found, string Cleaned, string Skipped, string Errors, string Recovered, string Note);

public sealed record DetailProblem(string Kind, string Where, string Message);

/// <summary>Read-only breakdown of the last scan and cleanup. Every figure is taken from the real reports.</summary>
public sealed class DetailsViewModel : PageViewModel
{
    private const int MaxProblems = 500;

    public DetailsViewModel(ScanReport scan, CleanReport? clean, Action back)
        : base("Details", back)
    {
        var cleaned = clean?.Modules.SelectMany(m => m.Categories).ToDictionary(c => (c.ModuleId, c.Category))
                      ?? new Dictionary<(string, string), CategoryResult>();

        var rows = new List<DetailRow>();
        var problems = new List<DetailProblem>();

        foreach (var module in scan.Modules)
        {
            if (module.Status == ModuleScanStatus.Disabled)
            {
                continue;
            }

            if (module.Findings.Count == 0)
            {
                rows.Add(new DetailRow(GroupStatusFormatter.Title(module.Group), module.ModuleName, module.Status.ToString(), "0", "—", "—", "—", "—", module.Message ?? string.Empty));
            }

            foreach (var category in module.Findings.GroupBy(f => f.Category))
            {
                var actionable = category.Where(f => f.IsEligible && f.Action != ActionType.None).ToList();
                var isRegistry = category.Any(f => f.Action == ActionType.RepairRegistryEntry);
                cleaned.TryGetValue((module.ModuleId, category.Key), out var result);
                rows.Add(new DetailRow(
                    GroupStatusFormatter.Title(module.Group),
                    module.ModuleName,
                    category.Key,
                    actionable.Count.ToString("N0"),
                    clean is null || isRegistry ? "—" : (result?.Cleaned ?? 0).ToString("N0"),
                    clean is null || isRegistry ? "—" : (result?.Skipped ?? 0).ToString("N0"),
                    clean is null || isRegistry ? "—" : (result?.Errors ?? 0).ToString("N0"),
                    clean is null || isRegistry ? ByteFormatter.Format(actionable.Sum(f => f.EstimatedBytes)) + " est." : ByteFormatter.Format(result?.RecoveredBytes ?? 0),
                    actionable.Count == 0 ? category.FirstOrDefault(f => !f.IsEligible)?.SkipReason ?? string.Empty : isRegistry ? "Reviewed separately" : string.Empty));
            }

            foreach (var sample in module.SkippedSamples)
            {
                problems.Add(new DetailProblem("Skipped during scan", sample.Location, sample.Reason));
            }

            foreach (var error in module.Errors)
            {
                problems.Add(new DetailProblem("Scan error", module.ModuleName, error));
            }
        }

        if (clean is not null)
        {
            foreach (var outcome in clean.Modules.SelectMany(m => m.Outcomes).Where(o => o.Kind != OutcomeKind.Cleaned))
            {
                problems.Add(new DetailProblem(outcome.Kind == OutcomeKind.Error ? "Error" : "Skipped", outcome.Location, outcome.Message ?? string.Empty));
            }
        }

        Rows = rows;
        TotalProblems = problems.Count;
        Problems = problems.Take(MaxProblems).ToList();
        Summary = clean is null
            ? $"Scan only · {ByteFormatter.Format(scan.EligibleBytes)} eligible · {scan.TotalItemsScanned:N0} items examined"
            : $"Recovered {ByteFormatter.Format(clean.BytesRecovered)} · {clean.ItemsCleaned:N0} cleaned · {clean.ItemsSkipped:N0} skipped · {clean.Errors:N0} errors · {clean.Duration.TotalSeconds:0.0} s" + (clean.WasCanceled ? " · canceled" : string.Empty);
        ProblemsNote = TotalProblems > MaxProblems ? $"Showing the first {MaxProblems:N0} of {TotalProblems:N0}. The full list is in the log." : string.Empty;
    }

    public IReadOnlyList<DetailRow> Rows { get; }

    public IReadOnlyList<DetailProblem> Problems { get; }

    public int TotalProblems { get; }

    public string Summary { get; }

    public string ProblemsNote { get; }

    public bool HasProblems => Problems.Count > 0;
}
