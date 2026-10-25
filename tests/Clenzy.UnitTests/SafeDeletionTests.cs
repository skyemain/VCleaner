using Clenzy.Core.Models;
using Clenzy.Core.Rules;
using Clenzy.Core.Services;
using Clenzy.TestSupport;
using Xunit;

namespace Clenzy.UnitTests;

public sealed class PathGuardTests
{
    [Theory]
    [InlineData("")]
    [InlineData("relative/path")]
    [InlineData("/tmp/../etc/passwd")]
    [InlineData("/tmp/a/*")]
    [InlineData("/tmp/a?b")]
    public void Rejects_unsafe_paths(string path)
    {
        Assert.False(PathGuard.TryCanonicalize(path, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Accepts_absolute_path_and_trims_trailing_separator()
    {
        Assert.True(PathGuard.TryCanonicalize(Path.GetTempPath(), out var canonical, out _));
        Assert.False(canonical.EndsWith(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void IsUnder_requires_a_separator_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "abc");
        Assert.True(PathGuard.IsUnder(root, Path.Combine(root, "x")));
        Assert.False(PathGuard.IsUnder(root, root + "def"));
        Assert.False(PathGuard.IsUnder(root, root));
        Assert.True(PathGuard.IsSameOrUnder(root, root));
    }
}

public sealed class ProtectedPathsTests
{
    [Fact]
    public void Personal_and_system_folders_are_forbidden_roots_and_temp_is_not()
    {
        using var h = new Harness();
        var p = h.Paths;
        foreach (var path in new[]
                 {
                     p.Documents, p.Desktop, p.Downloads, p.Pictures, p.Videos, p.Music, p.ProgramFiles, p.ProgramFilesX86,
                     p.SystemDirectory, Path.Combine(p.WindowsDirectory, "WinSxS"), Path.Combine(p.SystemDirectory, "winevt", "Logs"),
                     p.UserProfile, p.LocalAppData, p.SystemDriveRoot, p.WindowsDirectory
                 })
        {
            Assert.True(h.Protected.IsForbiddenRoot(path, out _), path);
        }

        Assert.False(h.Protected.IsForbiddenRoot(p.UserTemp, out _));
        Assert.False(h.Protected.IsForbiddenRoot(p.WindowsTemp, out _));
        Assert.True(h.Protected.IsForbiddenRoot(Path.Combine(p.Documents, "Projects", "tmp"), out _));
    }
}

public sealed class SafeDeletionServiceTests
{
    private static FileCleanRule Rule(string root, TimeSpan? age = null, string[]? patterns = null, bool recursive = true) => new()
    {
        Id = "t:" + root,
        Category = "Test",
        Description = "Test file",
        RootPath = root,
        MinAge = age ?? TimeSpan.Zero,
        Patterns = patterns ?? new[] { "*" },
        Recursive = recursive
    };

    private static List<ScanEntry> Entries(SafeDeletionService s, PreparedRoot root) =>
        s.Enumerate(root, DateTime.UtcNow, CancellationToken.None).ToList();

    [Fact]
    public void Prepare_rejects_protected_ancestor_missing_and_invalid_roots()
    {
        using var h = new Harness();
        var none = ExclusionPolicy.None;

        Assert.Null(h.Deletion.Prepare(Rule(h.Paths.Documents), none, out var reason1));
        Assert.Contains("Protected", reason1);

        Directory.CreateDirectory(h.Paths.UserProfile);
        Assert.Null(h.Deletion.Prepare(Rule(h.Paths.UserProfile), none, out _));
        Assert.Null(h.Deletion.Prepare(Rule(h.Paths.LocalAppData), none, out _));
        Assert.Null(h.Deletion.Prepare(Rule(Path.Combine(h.Paths.UserTemp, "..", "Documents")), none, out var traversal));
        Assert.Contains("traversal", traversal);

        Assert.Null(h.Deletion.Prepare(Rule(Path.Combine(h.Temp.Path, "missing")), none, out var missing));
        Assert.StartsWith("Not present", missing);

        Assert.Null(h.Deletion.Prepare(Rule(h.Paths.UserTemp, patterns: new[] { "..\\*" }), none, out _));
        Assert.Null(h.Deletion.Prepare(Rule(h.Paths.UserTemp, patterns: new[] { "sub/file" }), none, out _));
        Assert.NotNull(h.Deletion.Prepare(Rule(h.Paths.UserTemp), none, out _));
    }

    [Fact]
    public void Enumerate_reports_only_old_files_matching_patterns()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/old.tmp", 10, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/sub/deep.dat", 20, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/fresh.tmp", 30, DateTime.UtcNow);

        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp, TimeSpan.FromHours(24)), ExclusionPolicy.None, out _)!;
        var eligible = Entries(h.Deletion, root).Where(e => e.Eligibility.Kind == EligibilityKind.Eligible).ToList();

        Assert.Equal(2, eligible.Count);
        Assert.DoesNotContain(eligible, e => e.Path.EndsWith("fresh.tmp"));

        var rootPattern = h.Deletion.Prepare(Rule(h.Paths.UserTemp, patterns: new[] { "*.tmp" }), ExclusionPolicy.None, out _)!;
        Assert.Equal(2, Entries(h.Deletion, rootPattern).Count(e => e.Eligibility.Kind == EligibilityKind.Eligible));
    }

    [Fact]
    public void Deleting_removes_the_file_reports_real_bytes_and_prunes_empty_folders()
    {
        using var h = new Harness();
        var file = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/a/b/c.dat", 1234, Harness.OldUtc);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        var entry = Entries(h.Deletion, root).Single();

        var outcome = h.Deletion.DeleteFile(root, file, entry.LastWriteUtc.Ticks, entry.Length, DateTime.UtcNow);

        Assert.Equal(OutcomeKind.Cleaned, outcome.Kind);
        Assert.Equal(1234, outcome.Bytes);
        Assert.False(File.Exists(file));

        var removed = h.Deletion.PruneEmptyDirectories(root, new[] { Path.GetDirectoryName(file)! }, CancellationToken.None);
        Assert.Equal(2, removed);
        Assert.True(Directory.Exists(h.Paths.UserTemp));
    }

    [Fact]
    public void Locked_files_are_skipped_not_deleted()
    {
        using var h = new Harness();
        var file = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/locked.dat", 50, Harness.OldUtc);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        var entry = Entries(h.Deletion, root).Single();

        using var hold = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var outcome = h.Deletion.DeleteFile(root, file, entry.LastWriteUtc.Ticks, entry.Length, DateTime.UtcNow);

        Assert.Equal(OutcomeKind.Skipped, outcome.Kind);
        Assert.Contains("in use", outcome.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Path_traversal_and_foreign_paths_are_rejected_at_delete_time()
    {
        using var h = new Harness();
        var outside = h.Temp.CreateFile("Users/tester/Documents/precious.docx", 10, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/x.tmp", 10, Harness.OldUtc);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        var ticks = File.GetLastWriteTimeUtc(outside).Ticks;

        var traversal = Path.Combine(h.Paths.UserTemp, "..", "..", "..", "Documents", "precious.docx");
        Assert.Equal(OutcomeKind.Skipped, h.Deletion.DeleteFile(root, traversal, ticks, 10, DateTime.UtcNow).Kind);
        Assert.Equal(OutcomeKind.Skipped, h.Deletion.DeleteFile(root, outside, ticks, 10, DateTime.UtcNow).Kind);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Files_changed_since_the_scan_are_not_deleted()
    {
        using var h = new Harness();
        var file = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/c.dat", 10, Harness.OldUtc);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        var entry = Entries(h.Deletion, root).Single();

        File.AppendAllText(file, "more data");
        File.SetLastWriteTimeUtc(file, Harness.OldUtc);

        var outcome = h.Deletion.DeleteFile(root, file, entry.LastWriteUtc.Ticks, entry.Length, DateTime.UtcNow);
        Assert.Equal(OutcomeKind.Skipped, outcome.Kind);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Read_only_files_are_left_alone()
    {
        using var h = new Harness();
        var file = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/ro.dat", 10, Harness.OldUtc);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;

        var entry = Entries(h.Deletion, root).Single();
        Assert.Equal(EligibilityKind.Skipped, entry.Eligibility.Kind);
        Assert.Contains("Read-only", entry.Eligibility.Reason);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Symlinked_folders_are_never_traversed_and_targets_are_untouched()
    {
        using var h = new Harness();
        var victim = h.Temp.CreateFile("Users/tester/Documents/keep.docx", 10, Harness.OldUtc);
        var link = Path.Combine(h.Paths.UserTemp, "link");
        if (!TryCreateDirectorySymlink(link, Path.GetDirectoryName(victim)!))
        {
            return;
        }

        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/real.tmp", 10, Harness.OldUtc);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        var entries = Entries(h.Deletion, root);

        Assert.Single(entries, e => e.Eligibility.Kind == EligibilityKind.Eligible);
        Assert.DoesNotContain(entries, e => e.Path.EndsWith("keep.docx"));
        Assert.Contains(entries, e => e.Eligibility.Kind == EligibilityKind.Skipped && e.Path == link);

        var ticks = File.GetLastWriteTimeUtc(victim).Ticks;
        var viaLink = Path.Combine(link, "keep.docx");
        Assert.Equal(OutcomeKind.Skipped, h.Deletion.DeleteFile(root, viaLink, ticks, 10, DateTime.UtcNow).Kind);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void A_folder_swapped_for_a_link_after_the_scan_is_not_followed()
    {
        using var h = new Harness();
        var victim = h.Temp.CreateFile("Users/tester/Documents/keep.docx", 10, Harness.OldUtc);
        var file = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/dir/keep.docx", 10, Harness.OldUtc);
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        var entry = Entries(h.Deletion, root).Single();

        Directory.Delete(Path.Combine(h.Paths.UserTemp, "dir"), recursive: true);
        if (!TryCreateDirectorySymlink(Path.Combine(h.Paths.UserTemp, "dir"), Path.GetDirectoryName(victim)!))
        {
            return;
        }

        var outcome = h.Deletion.DeleteFile(root, file, entry.LastWriteUtc.Ticks, entry.Length, DateTime.UtcNow);
        Assert.Equal(OutcomeKind.Skipped, outcome.Kind);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void A_root_that_is_a_link_is_rejected()
    {
        using var h = new Harness();
        var target = Path.Combine(h.Temp.Path, "elsewhere");
        Directory.CreateDirectory(target);
        var link = Path.Combine(h.Paths.LocalAppData, "CacheLink");
        if (!TryCreateDirectorySymlink(link, target))
        {
            return;
        }

        Assert.Null(h.Deletion.Prepare(Rule(link), ExclusionPolicy.None, out var reason));
        Assert.Contains("not followed", reason);
    }

    [Fact]
    public void Excluded_files_and_folders_are_never_reported_or_deleted()
    {
        using var h = new Harness();
        var keepFile = h.Temp.CreateFile("Users/tester/AppData/Local/Temp/keep.dat", 10, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/skipdir/x.dat", 10, Harness.OldUtc);
        h.Temp.CreateFile("Users/tester/AppData/Local/Temp/go.dat", 10, Harness.OldUtc);

        var exclusions = new ExclusionPolicy(new[] { Path.Combine(h.Paths.UserTemp, "skipdir") }, new[] { keepFile });
        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), exclusions, out _)!;
        var entries = Entries(h.Deletion, root);

        Assert.Single(entries, e => e.Eligibility.Kind == EligibilityKind.Eligible && e.Path.EndsWith("go.dat"));
        Assert.DoesNotContain(entries, e => e.Eligibility.Kind == EligibilityKind.Eligible && e.Path.EndsWith("x.dat"));
        Assert.Equal(OutcomeKind.Skipped, h.Deletion.DeleteFile(root, keepFile, File.GetLastWriteTimeUtc(keepFile).Ticks, 10, DateTime.UtcNow).Kind);
        Assert.True(File.Exists(keepFile));
    }

    [Fact]
    public void Enumeration_honours_cancellation()
    {
        using var h = new Harness();
        for (var i = 0; i < 20; i++)
        {
            h.Temp.CreateFile($"Users/tester/AppData/Local/Temp/f{i}.dat", 1, Harness.OldUtc);
        }

        var root = h.Deletion.Prepare(Rule(h.Paths.UserTemp), ExclusionPolicy.None, out _)!;
        using var cts = new CancellationTokenSource();
        var seen = 0;

        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (var _ in h.Deletion.Enumerate(root, DateTime.UtcNow, cts.Token))
            {
                if (++seen == 3)
                {
                    cts.Cancel();
                }
            }
        });
        Assert.Equal(3, seen);
    }

    [Fact]
    public void Literal_file_rules_may_target_a_container_folder_but_only_exact_names()
    {
        using var h = new Harness();
        h.Temp.CreateFile("Windows/MEMORY.DMP", 10, Harness.OldUtc);
        h.Temp.CreateFile("Windows/notepad.exe", 10, Harness.OldUtc);

        var rule = Rule(h.Paths.WindowsDirectory, TimeSpan.FromDays(7), new[] { "MEMORY.DMP" }, recursive: false) with { AllowLiteralFilesInContainer = true };
        var root = h.Deletion.Prepare(rule, ExclusionPolicy.None, out _)!;
        var eligible = Entries(h.Deletion, root).Where(e => e.Eligibility.Kind == EligibilityKind.Eligible).ToList();
        Assert.Single(eligible);
        Assert.EndsWith("MEMORY.DMP", eligible[0].Path);

        Assert.Null(h.Deletion.Prepare(rule with { Recursive = true }, ExclusionPolicy.None, out _));
        Assert.Null(h.Deletion.Prepare(rule with { Patterns = new[] { "*.DMP" } }, ExclusionPolicy.None, out _));
        Assert.Null(h.Deletion.Prepare(Rule(h.Paths.WindowsDirectory), ExclusionPolicy.None, out _));
    }

    private static bool TryCreateDirectorySymlink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
