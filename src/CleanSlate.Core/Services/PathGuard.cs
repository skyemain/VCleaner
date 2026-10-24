namespace CleanSlate.Core.Services;

/// <summary>Canonical path validation helpers shared by every file-cleaning code path.</summary>
public static class PathGuard
{
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static StringComparer Comparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Produces a canonical absolute path. Rejects relative paths, traversal segments, wildcards,
    /// device/UNC paths and alternate data streams instead of silently "fixing" them.
    /// </summary>
    public static bool TryCanonicalize(string? path, out string canonical, out string? error)
    {
        canonical = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        if (path.Contains('\0') || path.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            error = "Path contains wildcard or invalid characters.";
            return false;
        }

        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment == "..")
            {
                error = "Path traversal is not allowed.";
                return false;
            }
        }

        if (!Path.IsPathFullyQualified(path))
        {
            error = "Path is not absolute.";
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            {
                error = "Network and device paths are not allowed.";
                return false;
            }

            if (path.IndexOf(':', 2) >= 0)
            {
                error = "Alternate data streams are not allowed.";
                return false;
            }
        }

        try
        {
            canonical = TrimTrailingSeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            error = "Path is not valid.";
            return false;
        }
    }

    public static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        if (path.Length <= root.Length)
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>True when <paramref name="path"/> equals or is a descendant of <paramref name="root"/> (component-wise).</summary>
    public static bool IsSameOrUnder(string root, string path)
    {
        root = TrimTrailingSeparator(root);
        path = TrimTrailingSeparator(path);

        if (path.Equals(root, Comparison))
        {
            return true;
        }

        return IsUnder(root, path);
    }

    /// <summary>True when <paramref name="path"/> is a strict descendant of <paramref name="root"/>.</summary>
    public static bool IsUnder(string root, string path)
    {
        root = TrimTrailingSeparator(root);
        if (path.Length <= root.Length || !path.StartsWith(root, Comparison))
        {
            return false;
        }

        var rootEndsWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar);
        if (rootEndsWithSeparator)
        {
            return true;
        }

        var next = path[root.Length];
        return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
    }

    public static int Depth(string path) =>
        path.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
}
