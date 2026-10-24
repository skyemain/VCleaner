using System.Text.RegularExpressions;
using CleanSlate.Core.Interfaces;
using CleanSlate.Core.Models;

namespace CleanSlate.Cleaners.Registry;

public enum TargetStatus
{
    /// <summary>The referenced executable exists (or a plausible interpretation of the command line exists).</summary>
    Exists,

    /// <summary>The executable is absolute, on a present fixed drive, and every interpretation is missing.</summary>
    Missing,

    /// <summary>The command cannot be verified (bare command, URI, UNC, removable drive, unresolved variable...). Never repaired.</summary>
    Unknown
}

public sealed record TargetResolution(TargetStatus Status, string? Path, string Detail);

/// <summary>
/// Resolves the executable referenced by a registry command line the way Windows does, but only reports
/// <see cref="TargetStatus.Missing"/> when the evidence is unambiguous.
/// All path handling is string based (Windows semantics) so the logic is identical on any host and is unit-testable.
/// </summary>
public sealed partial class CommandTargetResolver
{
    private static readonly string[] BareSearchExtensions = { string.Empty, ".exe", ".com", ".bat", ".cmd" };

    [GeneratedRegex("%[^%\\s]+%")]
    private static partial Regex UnresolvedVariablePattern();

    private readonly IFileProbe _probe;
    private readonly IEnvironmentExpander _env;

    public CommandTargetResolver(IFileProbe probe, IEnvironmentExpander env)
    {
        _probe = probe;
        _env = env;
    }

    /// <summary>Resolves the executable of a command line.</summary>
    /// <param name="commandLine">Raw registry data (unexpanded).</param>
    /// <param name="is32BitContext">True for entries in the 32-bit registry view, which are launched by 32-bit processes.</param>
    public TargetResolution ResolveCommand(string commandLine, bool is32BitContext)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return new TargetResolution(TargetStatus.Unknown, null, "Empty command.");
        }

        var variants = ExpandVariants(commandLine.Trim(), is32BitContext);
        if (variants.Count == 0)
        {
            return new TargetResolution(TargetStatus.Unknown, null, "Contains an unresolved environment variable.");
        }

        TargetResolution? firstMissing = null;
        foreach (var variant in variants)
        {
            var result = ResolveExpanded(variant);
            switch (result.Status)
            {
                case TargetStatus.Exists:
                    return result;
                case TargetStatus.Unknown:
                    return result;
                default:
                    firstMissing ??= result;
                    break;
            }
        }

        return firstMissing ?? new TargetResolution(TargetStatus.Unknown, null, "Could not be resolved.");
    }

    /// <summary>Resolves a plain path value (not a command line), for example an App Paths default value.</summary>
    public TargetResolution ResolvePath(string value, bool is32BitContext)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return ResolveCommand(value.Length == 0 ? value : "\"" + value + "\"", is32BitContext);
    }

    private List<string> ExpandVariants(string raw, bool is32)
    {
        var expanded = _env.Expand(raw);
        if (UnresolvedVariablePattern().IsMatch(expanded))
        {
            return new List<string>();
        }

        var variants = new List<string> { expanded };
        if (is32)
        {
            var pf = _env.Get("ProgramFiles");
            var pf86 = _env.Get("ProgramFiles(x86)");
            if (!string.IsNullOrEmpty(pf) && !string.IsNullOrEmpty(pf86) && expanded.Contains(pf, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(ReplaceIgnoreCase(expanded, pf, pf86));
            }

            var system32 = _env.Get("SystemRoot") is { Length: > 0 } root ? root.TrimEnd('\\') + @"\System32" : null;
            if (system32 is not null && expanded.Contains(system32, StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(ReplaceIgnoreCase(expanded, system32, system32[..^"System32".Length] + "SysWOW64"));
            }
        }

        return variants.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReplaceIgnoreCase(string text, string oldValue, string newValue) =>
        text.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);

    private TargetResolution ResolveExpanded(string command)
    {
        List<string> candidates;
        var quoted = command.StartsWith('"');

        if (quoted)
        {
            var end = command.IndexOf('"', 1);
            if (end < 0)
            {
                return new TargetResolution(TargetStatus.Unknown, null, "Unbalanced quotes.");
            }

            candidates = new List<string> { command[1..end].Trim() };
        }
        else
        {
            candidates = BuildUnquotedCandidates(command);
        }

        if (candidates.Count == 0 || candidates[0].Length == 0)
        {
            return new TargetResolution(TargetStatus.Unknown, null, "Empty command.");
        }

        var first = candidates[0];

        if (!IsDriveAbsolute(first))
        {
            return ResolveBare(first);
        }

        foreach (var candidate in candidates)
        {
            if (!IsDriveAbsolute(candidate))
            {
                continue;
            }

            if (ExistsAsFile(candidate))
            {
                return new TargetResolution(TargetStatus.Exists, candidate, "Found.");
            }
        }

        if (!_probe.IsOnFixedLocalDrive(first))
        {
            return new TargetResolution(TargetStatus.Unknown, first, "The drive is not a present fixed local drive.");
        }

        var evidence = quoted ? first : candidates.LastOrDefault(HasExecutableExtension) ?? first;
        return new TargetResolution(TargetStatus.Missing, evidence, "File not found.");
    }

    private TargetResolution ResolveBare(string name)
    {
        if (name.StartsWith(@"\\", StringComparison.Ordinal) || name.Contains(':') || name.Contains('/'))
        {
            return new TargetResolution(TargetStatus.Unknown, name, "Not a local file path.");
        }

        var directories = new List<string>();
        var root = _env.Get("SystemRoot");
        if (!string.IsNullOrEmpty(root))
        {
            directories.Add(root.TrimEnd('\\') + @"\System32");
            directories.Add(root.TrimEnd('\\'));
        }

        if (_env.Get("PATH") is { Length: > 0 } path)
        {
            directories.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().Trim('"')));
        }

        foreach (var dir in directories.Where(d => d.Length > 0))
        {
            foreach (var ext in BareSearchExtensions)
            {
                if (_probe.FileExists(dir.TrimEnd('\\') + "\\" + name + ext))
                {
                    return new TargetResolution(TargetStatus.Exists, name, "Found on the search path.");
                }
            }
        }

        return new TargetResolution(TargetStatus.Unknown, name, "Command name only; cannot be verified.");
    }

    /// <summary>Progressively joins space separated tokens, mirroring how CreateProcess probes unquoted paths.</summary>
    private static List<string> BuildUnquotedCandidates(string command)
    {
        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>();
        var current = string.Empty;
        foreach (var token in tokens)
        {
            current = current.Length == 0 ? token : current + " " + token;
            list.Add(current);
            if (list.Count >= 12)
            {
                break;
            }
        }

        return list;
    }

    private bool ExistsAsFile(string path)
    {
        path = path.Replace('/', '\\');
        if (_probe.FileExists(path))
        {
            return true;
        }

        var name = path[(path.LastIndexOf('\\') + 1)..];
        return !name.Contains('.') && _probe.FileExists(path + ".exe");
    }

    private static bool HasExecutableExtension(string candidate)
    {
        var dot = candidate.LastIndexOf('.');
        if (dot < 0 || candidate.LastIndexOf('\\') > dot)
        {
            return false;
        }

        var ext = candidate[dot..].ToLowerInvariant();
        return ext is ".exe" or ".com" or ".bat" or ".cmd" or ".scr" or ".msc" or ".cpl" or ".lnk" or ".vbs" or ".js" or ".jar" or ".ps1" or ".msi";
    }

    public static bool IsDriveAbsolute(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    public bool IsUnderWindowsDirectory(string path)
    {
        var root = _env.Get("SystemRoot");
        return !string.IsNullOrEmpty(root) &&
               (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
    }
}
