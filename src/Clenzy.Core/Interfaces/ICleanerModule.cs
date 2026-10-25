using Clenzy.Core.Configuration;
using Clenzy.Core.Models;
using Clenzy.Core.Services;

namespace Clenzy.Core.Interfaces;

/// <summary>Receives thread-safe incremental progress from a module.</summary>
public interface IModuleReporter
{
    void Operation(string text);
    void Scanned(long items, long bytes);
    void Processed(int actions, long bytesRecovered, int skipped, int errors);
}

public sealed class NullModuleReporter : IModuleReporter
{
    public static NullModuleReporter Instance { get; } = new();
    public void Operation(string text) { }
    public void Scanned(long items, long bytes) { }
    public void Processed(int actions, long bytesRecovered, int skipped, int errors) { }
}

public sealed class ScanContext
{
    public required ClenzySettings Settings { get; init; }
    public required IModuleReporter Reporter { get; init; }
    public required ClaimSet Claims { get; init; }
    public required ExclusionPolicy Exclusions { get; init; }
    public bool IsElevated { get; init; }
    public DateTime NowUtc { get; init; } = DateTime.UtcNow;
    public int MaxFindingsPerModule { get; init; } = 100_000;
}

public sealed class CleanContext
{
    public required ClenzySettings Settings { get; init; }
    public required IModuleReporter Reporter { get; init; }
    public required ExclusionPolicy Exclusions { get; init; }
    public bool IsElevated { get; init; }
    public DateTime NowUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>Common contract implemented by every cleaning module.</summary>
public interface ICleanerModule
{
    string ModuleId { get; }
    string Name { get; }
    string Description { get; }
    CleanerGroup Group { get; }
    RiskLevel RiskLevel { get; }
    bool RequiresElevation { get; }
    bool SupportsPreview { get; }

    /// <summary>Whether the module participates in a run for the given settings.</summary>
    bool IsEnabled(ClenzySettings settings);

    /// <summary>Read-only discovery. Must never modify the file system or registry.</summary>
    Task<ModuleScanResult> ScanAsync(ScanContext context, CancellationToken cancellationToken);

    /// <summary>Executes the supplied findings after revalidating each one.</summary>
    Task<ModuleCleanResult> CleanAsync(CleanContext context, IReadOnlyList<Finding> findings, CancellationToken cancellationToken);

    /// <summary>Checks that a single finding is still valid and safe to act on right now.</summary>
    Task<ValidationResult> ValidateAsync(Finding finding, CleanContext context, CancellationToken cancellationToken);
}
