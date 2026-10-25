using System.Collections.Concurrent;
using Clenzy.Core.Configuration;

namespace Clenzy.Core.Services;

/// <summary>User-defined excluded folders and files. Excluded locations are never scanned or deleted.</summary>
public sealed class ExclusionPolicy
{
    public static ExclusionPolicy None { get; } = new(Array.Empty<string>(), Array.Empty<string>());

    private readonly string[] _folders;
    private readonly HashSet<string> _files;

    public ExclusionPolicy(IEnumerable<string> folders, IEnumerable<string> files)
    {
        _folders = folders.Select(Canon).Where(p => p is not null).Select(p => p!).ToArray();
        _files = new HashSet<string>(files.Select(Canon).Where(p => p is not null).Select(p => p!), PathGuard.Comparer);
    }

    public static ExclusionPolicy FromSettings(ClenzySettings settings) =>
        new(settings.ExcludedFolders, settings.ExcludedFiles);

    public bool IsEmpty => _folders.Length == 0 && _files.Count == 0;

    public bool IsExcluded(string canonicalPath)
    {
        if (_files.Contains(canonicalPath))
        {
            return true;
        }

        foreach (var folder in _folders)
        {
            if (PathGuard.IsSameOrUnder(folder, canonicalPath))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Canon(string path)
    {
        var value = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return PathGuard.TryCanonicalize(value, out var canonical, out _) ? canonical : null;
    }
}

/// <summary>
/// Tracks canonical paths already attributed to a finding so that two modules whose roots overlap
/// never count (or delete) the same file twice.
/// </summary>
public sealed class ClaimSet
{
    private readonly ConcurrentDictionary<string, byte> _claims = new(PathGuard.Comparer);

    public bool TryClaim(string canonicalPath) => _claims.TryAdd(canonicalPath, 0);

    public int Count => _claims.Count;
}
