using System.IO.Enumeration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Clenzy.Core.Rules;

namespace Clenzy.Core.Services;

public enum EligibilityKind
{
    Eligible,

    /// <summary>The file does not match the rule's patterns. Not reported.</summary>
    NotMatching,

    /// <summary>The file is newer than the rule's minimum age. Not reported.</summary>
    TooRecent,

    /// <summary>The file matched but must be left alone; reported as skipped.</summary>
    Skipped
}

public readonly record struct Eligibility(EligibilityKind Kind, string? Reason)
{
    public static Eligibility Ok { get; } = new(EligibilityKind.Eligible, null);
}

public readonly record struct FileSnapshot(string Path, string Name, long Length, DateTime LastWriteUtc, FileAttributes Attributes);

/// <summary>One entry produced while streaming a root.</summary>
public readonly record struct ScanEntry(string Path, long Length, DateTime LastWriteUtc, Eligibility Eligibility);

public readonly record struct DeletionOutcome(OutcomeKind Kind, string? Message, long Bytes)
{
    public static DeletionOutcome Cleaned(long bytes) => new(OutcomeKind.Cleaned, null, bytes);
    public static DeletionOutcome Skipped(string reason) => new(OutcomeKind.Skipped, reason, 0);
    public static DeletionOutcome Failed(string reason) => new(OutcomeKind.Error, reason, 0);
}

/// <summary>A rule whose root passed validation. Only prepared roots can be scanned or cleaned.</summary>
public sealed class PreparedRoot
{
    internal PreparedRoot(FileCleanRule rule, string path, ExclusionPolicy exclusions)
    {
        Rule = rule;
        Path = path;
        Exclusions = exclusions;
    }

    public FileCleanRule Rule { get; }
    public string Path { get; }
    public ExclusionPolicy Exclusions { get; }
}

public interface ISafeDeletionService
{
    /// <summary>Validates a rule and its root. Returns null (with a reason) when the rule must not run.</summary>
    PreparedRoot? Prepare(FileCleanRule rule, ExclusionPolicy exclusions, out string? skipReason);

    /// <summary>Streams every file under the root together with its eligibility. Never modifies anything.</summary>
    IEnumerable<ScanEntry> Enumerate(PreparedRoot root, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>The single eligibility decision shared by preview (scan) and execution (clean).</summary>
    Eligibility Evaluate(PreparedRoot root, in FileSnapshot file, DateTime nowUtc);

    /// <summary>Revalidates and permanently deletes one file inside an approved root.</summary>
    DeletionOutcome DeleteFile(PreparedRoot root, string path, long expectedLastWriteTicks, long expectedLength, DateTime nowUtc);

    /// <summary>Removes directories that became empty, from the supplied starting points upward, never the root itself.</summary>
    int PruneEmptyDirectories(PreparedRoot root, IEnumerable<string> startDirectories, CancellationToken cancellationToken);
}

/// <summary>
/// Safe deletion service used by every file-cleaning module. It enforces approved roots, a protected-path
/// denylist, canonical path checks, reparse-point refusal, identity re-checks and lock detection.
/// </summary>
public sealed class SafeDeletionService : ISafeDeletionService
{
    private readonly ProtectedPaths _protected;

    public SafeDeletionService(ProtectedPaths protectedPaths)
    {
        _protected = protectedPaths;
    }

    public PreparedRoot? Prepare(FileCleanRule rule, ExclusionPolicy exclusions, out string? skipReason)
    {
        skipReason = null;

        if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Category))
        {
            skipReason = "Invalid rule definition.";
            return null;
        }

        foreach (var pattern in rule.Patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern) ||
                pattern.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 ||
                pattern.Contains("..", StringComparison.Ordinal))
            {
                skipReason = "Invalid file pattern in rule.";
                return null;
            }
        }

        if (rule.Patterns.Count == 0 || rule.MinAge < TimeSpan.Zero)
        {
            skipReason = "Invalid rule definition.";
            return null;
        }

        if (!PathGuard.TryCanonicalize(rule.RootPath, out var root, out var error))
        {
            skipReason = $"Rejected root: {error}";
            return null;
        }

        if (rule.AllowLiteralFilesInContainer)
        {
            if (rule.Recursive || rule.Patterns.Any(p => p.IndexOfAny(new[] { '*', '?' }) >= 0))
            {
                skipReason = "Invalid rule definition.";
                return null;
            }

            if (_protected.IsInsideProtectedSubtree(root, out var containerReason))
            {
                skipReason = $"Rejected root: {containerReason}";
                return null;
            }
        }
        else if (_protected.IsForbiddenRoot(root, out var protectedReason))
        {
            skipReason = $"Rejected root: {protectedReason}";
            return null;
        }

        if (exclusions.IsExcluded(root))
        {
            skipReason = "Excluded by your settings.";
            return null;
        }

        DirectoryInfo info;
        try
        {
            info = new DirectoryInfo(root);
            if (!info.Exists)
            {
                skipReason = "Not present on this system.";
                return null;
            }

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                skipReason = "Folder is a junction or symbolic link and is not followed.";
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            skipReason = "Folder is not accessible.";
            return null;
        }

        return new PreparedRoot(rule, root, exclusions);
    }

    public Eligibility Evaluate(PreparedRoot root, in FileSnapshot file, DateTime nowUtc)
    {
        var matches = false;
        foreach (var pattern in root.Rule.Patterns)
        {
            if (pattern == "*" || FileSystemName.MatchesSimpleExpression(pattern, file.Name, ignoreCase: true))
            {
                matches = true;
                break;
            }
        }

        if (!matches)
        {
            return new Eligibility(EligibilityKind.NotMatching, null);
        }

        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return new Eligibility(EligibilityKind.Skipped, "Symbolic link or junction (not followed)");
        }

        if (!PathGuard.IsUnder(root.Path, file.Path))
        {
            return new Eligibility(EligibilityKind.Skipped, "Outside the approved folder");
        }

        if (_protected.IsInsideProtectedSubtree(file.Path, out var protectedReason))
        {
            return new Eligibility(EligibilityKind.Skipped, protectedReason);
        }

        if (root.Exclusions.IsExcluded(file.Path))
        {
            return new Eligibility(EligibilityKind.Skipped, "Excluded by your settings");
        }

        if (root.Rule.MinAge > TimeSpan.Zero && nowUtc - file.LastWriteUtc < root.Rule.MinAge)
        {
            return new Eligibility(EligibilityKind.TooRecent, null);
        }

        if ((file.Attributes & FileAttributes.ReadOnly) != 0)
        {
            return new Eligibility(EligibilityKind.Skipped, "Read-only file (left untouched)");
        }

        if ((file.Attributes & (FileAttributes.System | FileAttributes.Offline)) != 0)
        {
            return new Eligibility(EligibilityKind.Skipped, "System or cloud placeholder file (left untouched)");
        }

        return Eligibility.Ok;
    }

    public IEnumerable<ScanEntry> Enumerate(PreparedRoot root, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };

        var pending = new Stack<string>();
        pending.Push(root.Path);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            var entries = ListDirectory(directory, options);

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = System.IO.Path.Combine(directory, entry.Name);

                if (entry.IsDirectory)
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        yield return new ScanEntry(fullPath, 0, entry.LastWriteUtc,
                            new Eligibility(EligibilityKind.Skipped, "Junction or symbolic link (not followed)"));
                    }
                    else if (root.Exclusions.IsExcluded(fullPath))
                    {
                        yield return new ScanEntry(fullPath, 0, entry.LastWriteUtc,
                            new Eligibility(EligibilityKind.Skipped, "Excluded by your settings"));
                    }
                    else if (root.Rule.Recursive)
                    {
                        pending.Push(fullPath);
                    }

                    continue;
                }

                var snapshot = new FileSnapshot(fullPath, entry.Name, entry.Length, entry.LastWriteUtc, entry.Attributes);
                yield return new ScanEntry(fullPath, entry.Length, entry.LastWriteUtc, Evaluate(root, snapshot, nowUtc));
            }
        }
    }

    private readonly record struct RawEntry(string Name, bool IsDirectory, FileAttributes Attributes, long Length, DateTime LastWriteUtc);

    private static List<RawEntry> ListDirectory(string directory, EnumerationOptions options)
    {
        var list = new List<RawEntry>();
        try
        {
            var enumerable = new FileSystemEnumerable<RawEntry>(
                directory,
                (ref FileSystemEntry e) => new RawEntry(
                    e.FileName.ToString(),
                    e.IsDirectory,
                    e.Attributes,
                    e.IsDirectory ? 0 : e.Length,
                    e.LastWriteTimeUtc.UtcDateTime),
                options);

            list.AddRange(enumerable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Inaccessible or vanished directory: report nothing for it.
        }

        return list;
    }

    public DeletionOutcome DeleteFile(PreparedRoot root, string path, long expectedLastWriteTicks, long expectedLength, DateTime nowUtc)
    {
        if (!PathGuard.TryCanonicalize(path, out var canonical, out var error))
        {
            return DeletionOutcome.Skipped($"Rejected path: {error}");
        }

        if (!PathGuard.IsUnder(root.Path, canonical))
        {
            return DeletionOutcome.Skipped("Outside the approved folder");
        }

        if (_protected.IsInsideProtectedSubtree(canonical, out var protectedReason))
        {
            return DeletionOutcome.Skipped(protectedReason!);
        }

        try
        {
            var rootInfo = new DirectoryInfo(root.Path);
            if (!rootInfo.Exists || (rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return DeletionOutcome.Skipped("Approved folder changed since the scan");
            }

            var parent = System.IO.Path.GetDirectoryName(canonical);
            while (parent is not null && PathGuard.IsUnder(root.Path, parent))
            {
                var dir = new DirectoryInfo(parent);
                if (!dir.Exists)
                {
                    return DeletionOutcome.Skipped("No longer exists");
                }

                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return DeletionOutcome.Skipped("Parent folder is a junction or symbolic link");
                }

                parent = System.IO.Path.GetDirectoryName(parent);
            }

            var file = new FileInfo(canonical);
            if (!file.Exists)
            {
                return DeletionOutcome.Skipped("No longer exists");
            }

            if (file.LastWriteTimeUtc.Ticks != expectedLastWriteTicks || file.Length != expectedLength)
            {
                return DeletionOutcome.Skipped("Changed since the scan");
            }

            var snapshot = new FileSnapshot(canonical, file.Name, file.Length, file.LastWriteTimeUtc, file.Attributes);
            var eligibility = Evaluate(root, snapshot, nowUtc);
            if (eligibility.Kind != EligibilityKind.Eligible)
            {
                return DeletionOutcome.Skipped(eligibility.Reason ?? "Modified recently");
            }

            var probe = ProbeLock(canonical);
            if (probe is not null)
            {
                return probe.Value;
            }

            var length = file.Length;
            file.Delete();
            file.Refresh();
            return file.Exists
                ? DeletionOutcome.Failed("File still exists after deletion")
                : DeletionOutcome.Cleaned(length);
        }
        catch (UnauthorizedAccessException)
        {
            return DeletionOutcome.Skipped("Access denied (may require administrator)");
        }
        catch (FileNotFoundException)
        {
            return DeletionOutcome.Skipped("No longer exists");
        }
        catch (DirectoryNotFoundException)
        {
            return DeletionOutcome.Skipped("No longer exists");
        }
        catch (IOException ex) when (IsSharingViolation(ex))
        {
            return DeletionOutcome.Skipped("In use by another process");
        }
        catch (IOException ex)
        {
            return DeletionOutcome.Failed(Sanitize(ex.Message));
        }
    }

    private static DeletionOutcome? ProbeLock(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return DeletionOutcome.Skipped("Access denied (may require administrator)");
        }
        catch (FileNotFoundException)
        {
            return DeletionOutcome.Skipped("No longer exists");
        }
        catch (DirectoryNotFoundException)
        {
            return DeletionOutcome.Skipped("No longer exists");
        }
        catch (IOException)
        {
            return DeletionOutcome.Skipped("In use by another process");
        }
    }

    private static bool IsSharingViolation(IOException ex)
    {
        const int sharingViolation = 32;
        const int lockViolation = 33;
        var code = ex.HResult & 0xFFFF;
        return code is sharingViolation or lockViolation;
    }

    private static string Sanitize(string message) => message.Length > 200 ? message[..200] : message;

    public int PruneEmptyDirectories(PreparedRoot root, IEnumerable<string> startDirectories, CancellationToken cancellationToken)
    {
        if (!root.Rule.PruneEmptyDirectories)
        {
            return 0;
        }

        var removed = 0;

        foreach (var start in startDirectories.Distinct(PathGuard.Comparer).OrderByDescending(PathGuard.Depth))
        {
            var current = start;
            while (current is not null && PathGuard.IsUnder(root.Path, current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var dir = new DirectoryInfo(current);
                    if (!dir.Exists)
                    {
                        current = System.IO.Path.GetDirectoryName(current);
                        continue;
                    }

                    if ((dir.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        root.Exclusions.IsExcluded(current) ||
                        _protected.IsInsideProtectedSubtree(current, out _) ||
                        dir.EnumerateFileSystemInfos().Any())
                    {
                        break;
                    }

                    dir.Delete(recursive: false);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                current = System.IO.Path.GetDirectoryName(current);
            }
        }

        return removed;
    }
}
