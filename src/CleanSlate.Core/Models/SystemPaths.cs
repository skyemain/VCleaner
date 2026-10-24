namespace CleanSlate.Core.Models;

/// <summary>
/// Well-known locations the cleaners use. Production values come from the Windows layer;
/// tests construct instances that point into temporary directories.
/// </summary>
public sealed record SystemPaths
{
    public required string SystemDriveRoot { get; init; }
    public required string UserProfile { get; init; }
    public required string LocalAppData { get; init; }
    public required string RoamingAppData { get; init; }
    public required string ProgramData { get; init; }
    public required string WindowsDirectory { get; init; }
    public required string SystemDirectory { get; init; }
    public required string ProgramFiles { get; init; }
    public required string ProgramFilesX86 { get; init; }
    public required string UserTemp { get; init; }
    public string? UserTmp { get; init; }
    public required string Documents { get; init; }
    public required string Desktop { get; init; }
    public required string Downloads { get; init; }
    public required string Pictures { get; init; }
    public required string Videos { get; init; }
    public required string Music { get; init; }

    /// <summary>Cloud-sync roots (for example OneDrive) that must never be cleaned.</summary>
    public IReadOnlyList<string> ExtraProtectedRoots { get; init; } = Array.Empty<string>();

    public string WindowsTemp => Path.Combine(WindowsDirectory, "Temp");
}
