using Clenzy.Core.Models;

namespace Clenzy.Core.Rules;

/// <summary>
/// An explicit, validated definition of a file-cleaning target: one approved root, simple file-name
/// patterns, a minimum age and a risk classification. Rules are compiled into the application;
/// the application never loads cleaning definitions from external files or the network.
/// </summary>
public sealed record FileCleanRule
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }

    /// <summary>The approved root directory. Must be absolute and must not contain wildcards.</summary>
    public required string RootPath { get; init; }

    /// <summary>Simple file-name patterns (no path separators). "*" matches every file under the root.</summary>
    public IReadOnlyList<string> Patterns { get; init; } = new[] { "*" };

    public bool Recursive { get; init; } = true;

    /// <summary>Files modified more recently than this are not candidates.</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;

    public RiskLevel Risk { get; init; } = RiskLevel.Safe;
    public bool AutoCleanAllowed { get; init; } = true;
    public bool ConfirmationRequired { get; init; }
    public bool PruneEmptyDirectories { get; init; } = true;

    /// <summary>
    /// Allows the root to be a protected container folder (for example the Windows directory) strictly for
    /// literal file names. Requires <see cref="Recursive"/> to be false and no wildcard patterns.
    /// </summary>
    public bool AllowLiteralFilesInContainer { get; init; }
}
