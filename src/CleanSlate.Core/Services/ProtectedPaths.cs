using CleanSlate.Core.Models;

namespace CleanSlate.Core.Services;

/// <summary>
/// Denylist of locations that no cleaning rule may use as a root or descend into:
/// personal folders, program files, Windows system components and security stores.
/// </summary>
public sealed class ProtectedPaths
{
    private readonly List<string> _subtrees = new();
    private readonly List<string> _exact = new();

    public ProtectedPaths(SystemPaths paths)
    {
        var win = paths.WindowsDirectory;

        AddSubtree(paths.Documents, paths.Desktop, paths.Downloads, paths.Pictures, paths.Videos, paths.Music);
        AddSubtree(paths.ProgramFiles, paths.ProgramFilesX86);
        // Windows\Prefetch is deliberately not listed: the prefetch cleaner is restricted to top-level *.pf files by its rule,
        // so sub-folders such as ReadyBoot and every other file type stay untouched.
        AddSubtree(paths.SystemDirectory);
        AddSubtree(
            Path.Combine(win, "SysWOW64"),
            Path.Combine(win, "WinSxS"),
            Path.Combine(win, "servicing"),
            Path.Combine(win, "Installer"),
            Path.Combine(win, "assembly"),
            Path.Combine(win, "Boot"),
            Path.Combine(win, "Fonts"),
            Path.Combine(win, "SoftwareDistribution"),
            Path.Combine(win, "Microsoft.NET"),
            Path.Combine(win, "System"),
            Path.Combine(win, "SystemApps"),
            Path.Combine(win, "Security"),
            Path.Combine(win, "Logs", "SIH"));
        AddSubtree(
            Path.Combine(paths.ProgramData, "Microsoft", "Windows Defender"),
            Path.Combine(paths.ProgramData, "Microsoft", "Crypto"),
            Path.Combine(paths.ProgramData, "Package Cache"),
            Path.Combine(paths.ProgramData, "Microsoft", "Windows", "Start Menu"));
        AddSubtree(
            Path.Combine(paths.RoamingAppData, "Microsoft", "Credentials"),
            Path.Combine(paths.RoamingAppData, "Microsoft", "Protect"),
            Path.Combine(paths.LocalAppData, "Microsoft", "Credentials"),
            Path.Combine(paths.LocalAppData, "Microsoft", "Vault"),
            Path.Combine(paths.UserProfile, ".ssh"));
        AddSubtree(
            Path.Combine(paths.SystemDriveRoot, "Recovery"),
            Path.Combine(paths.SystemDriveRoot, "System Volume Information"),
            Path.Combine(paths.SystemDriveRoot, "$Recycle.Bin"),
            Path.Combine(paths.SystemDriveRoot, "Boot"),
            Path.Combine(paths.SystemDriveRoot, "EFI"));
        AddSubtree(paths.ExtraProtectedRoots.ToArray());

        _exact.Add(Normalize(paths.SystemDriveRoot));
        AddExact(
            paths.UserProfile,
            Path.GetDirectoryName(paths.UserProfile.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            paths.LocalAppData,
            paths.RoamingAppData,
            paths.ProgramData,
            paths.WindowsDirectory);
    }

    private static string Normalize(string path) => PathGuard.TrimTrailingSeparator(path);

    private void AddSubtree(params string[] paths)
    {
        foreach (var p in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            _subtrees.Add(Normalize(p));
        }
    }

    private void AddExact(params string?[] paths)
    {
        foreach (var p in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            _exact.Add(Normalize(p!));
        }
    }

    /// <summary>True when the path is inside (or equal to) a protected subtree.</summary>
    public bool IsInsideProtectedSubtree(string canonicalPath, out string? reason)
    {
        foreach (var protectedPath in _subtrees)
        {
            if (PathGuard.IsSameOrUnder(protectedPath, canonicalPath))
            {
                reason = $"Protected location ({protectedPath})";
                return true;
            }
        }

        reason = null;
        return false;
    }

    /// <summary>
    /// A cleaning root must not be a protected folder, inside one, a well-known container
    /// (drive root, user profile, AppData, Windows) or an ancestor of any of them.
    /// </summary>
    public bool IsForbiddenRoot(string canonicalPath, out string? reason)
    {
        if (IsInsideProtectedSubtree(canonicalPath, out reason))
        {
            return true;
        }

        foreach (var exact in _exact)
        {
            if (canonicalPath.Equals(exact, PathGuard.Comparison))
            {
                reason = $"Protected container ({exact})";
                return true;
            }
        }

        foreach (var candidate in _subtrees.Concat(_exact))
        {
            if (PathGuard.IsUnder(canonicalPath, candidate))
            {
                reason = $"Contains protected location ({candidate})";
                return true;
            }
        }

        reason = null;
        return false;
    }
}
