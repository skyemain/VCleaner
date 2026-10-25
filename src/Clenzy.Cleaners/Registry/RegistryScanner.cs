using System.Security.Cryptography;
using System.Text;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;

namespace Clenzy.Cleaners.Registry;

/// <summary>
/// The only registry locations Clenzy scans or repairs. Production values are fixed; tests supply isolated keys
/// so nothing in a real hive is ever touched by automated tests.
/// </summary>
public sealed class RegistryScanLocations
{
    public IReadOnlyList<RegistryKeyRef> StartupKeys { get; init; } = Array.Empty<RegistryKeyRef>();
    public IReadOnlyList<RegistryKeyRef> AppPathsKeys { get; init; } = Array.Empty<RegistryKeyRef>();
    public IReadOnlyList<RegistryKeyRef> UninstallKeys { get; init; } = Array.Empty<RegistryKeyRef>();

    public static RegistryScanLocations Production { get; } = new()
    {
        StartupKeys = new[]
        {
            new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Run"),
            new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
        },
        AppPathsKeys = new[]
        {
            new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Microsoft\Windows\CurrentVersion\App Paths"),
            new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths")
        },
        UninstallKeys = new[]
        {
            new RegistryKeyRef(RegistryHiveKind.CurrentUser, RegistryViewKind.Registry64, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
            new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            new RegistryKeyRef(RegistryHiveKind.LocalMachine, RegistryViewKind.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        }
    };
}

public sealed record RegistryScanResult(IReadOnlyList<RegistryIssue> Issues, long EntriesExamined, IReadOnlyList<string> Errors);

/// <summary>Read-only registry diagnostics for three narrowly defined, evidence-based rules.</summary>
public sealed class RegistryScanner
{
    private static readonly string[] ForbiddenSegments =
    {
        "CLSID", "Classes", "Installer", "SharedDLLs", "Services", "Drivers", "Component Based Servicing",
        "Windows Defender", "FileExts", "Policies", "Security", "Winlogon"
    };

    private readonly IRegistryAccess _registry;
    private readonly IEnvironmentExpander _env;
    private readonly RegistryScanLocations _locations;
    private readonly CommandTargetResolver _resolver;
    private readonly IFileProbe _probe;

    public RegistryScanner(IRegistryAccess registry, IFileProbe probe, IEnvironmentExpander env, RegistryScanLocations? locations = null)
    {
        _registry = registry;
        _probe = probe;
        _env = env;
        _locations = locations ?? RegistryScanLocations.Production;
        _resolver = new CommandTargetResolver(probe, env);
    }

    public RegistryScanLocations Locations => _locations;

    public RegistryScanResult Scan(CancellationToken ct)
    {
        var issues = new List<RegistryIssue>();
        var errors = new List<string>();
        long examined = 0;

        foreach (var key in _locations.StartupKeys)
        {
            ct.ThrowIfCancellationRequested();
            Guarded(errors, key, () =>
            {
                foreach (var name in _registry.GetValueNames(key))
                {
                    examined++;
                    var issue = EvaluateStartupValue(key, name);
                    if (issue is not null)
                    {
                        issues.Add(issue);
                    }
                }
            });
        }

        foreach (var key in _locations.AppPathsKeys)
        {
            ct.ThrowIfCancellationRequested();
            Guarded(errors, key, () =>
            {
                foreach (var sub in _registry.GetSubKeyNames(key))
                {
                    ct.ThrowIfCancellationRequested();
                    examined++;
                    var issue = EvaluateAppPath(key, sub);
                    if (issue is not null)
                    {
                        issues.Add(issue);
                    }
                }
            });
        }

        foreach (var key in _locations.UninstallKeys)
        {
            ct.ThrowIfCancellationRequested();
            Guarded(errors, key, () =>
            {
                foreach (var sub in _registry.GetSubKeyNames(key))
                {
                    ct.ThrowIfCancellationRequested();
                    examined++;
                    var issue = EvaluateUninstall(key, sub);
                    if (issue is not null)
                    {
                        issues.Add(issue);
                    }
                }
            });
        }

        return new RegistryScanResult(issues, examined, errors);
    }

    private static void Guarded(List<string> errors, RegistryKeyRef key, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            errors.Add($"{key.DisplayPath}: {ex.Message}");
        }
    }

    /// <summary>Re-reads one entry and re-applies its rule. Returns null if it is no longer a valid finding.</summary>
    public RegistryIssue? Revalidate(RegistryIssue issue)
    {
        try
        {
            RegistryIssue? current = issue.Rule switch
            {
                RegistryRuleKind.StartupEntry when issue.ValueName is not null && IsConfiguredKey(_locations.StartupKeys, issue.Key) =>
                    EvaluateStartupValue(issue.Key, issue.ValueName),
                RegistryRuleKind.AppPathsEntry when issue.ValueName is null && TryParent(_locations.AppPathsKeys, issue.Key, out var appParent, out var appName) =>
                    EvaluateAppPath(appParent, appName),
                RegistryRuleKind.OrphanedUninstallEntry when issue.ValueName is null && TryParent(_locations.UninstallKeys, issue.Key, out var unParent, out var unName) =>
                    EvaluateUninstall(unParent, unName),
                _ => null
            };

            if (current is null)
            {
                return null;
            }

            if (issue.ObservedData is not null && !issue.ObservedData.ValueEquals(current.ObservedData))
            {
                return null;
            }

            return current;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return null;
        }
    }

    private static bool IsConfiguredKey(IReadOnlyList<RegistryKeyRef> keys, RegistryKeyRef key) =>
        keys.Any(k => k.Hive == key.Hive && k.View == key.View && string.Equals(k.Path, key.Path, StringComparison.OrdinalIgnoreCase));

    private static bool TryParent(IReadOnlyList<RegistryKeyRef> parents, RegistryKeyRef key, out RegistryKeyRef parent, out string childName)
    {
        foreach (var p in parents)
        {
            if (p.Hive == key.Hive && p.View == key.View &&
                key.Path.StartsWith(p.Path + "\\", StringComparison.OrdinalIgnoreCase) &&
                key.Path.IndexOf('\\', p.Path.Length + 1) < 0)
            {
                parent = p;
                childName = key.Path[(p.Path.Length + 1)..];
                return true;
            }
        }

        parent = default;
        childName = string.Empty;
        return false;
    }

    /// <summary>Allow-list check used before any repair or restore: only exact entries produced by the three rules.</summary>
    public bool IsSupportedTarget(RegistryRuleKind rule, RegistryKeyRef key, string? valueName, RegistryRepairAction action)
    {
        if (ContainsForbiddenSegment(key.Path))
        {
            // Tests use isolated keys that never contain these names; production locations never do either.
            if (!IsUnderConfiguredLocation(key))
            {
                return false;
            }
        }

        return rule switch
        {
            RegistryRuleKind.StartupEntry =>
                action == RegistryRepairAction.DeleteValue && !string.IsNullOrEmpty(valueName) && IsConfiguredKey(_locations.StartupKeys, key),
            RegistryRuleKind.AppPathsEntry =>
                action == RegistryRepairAction.DeleteKey && valueName is null && TryParent(_locations.AppPathsKeys, key, out _, out var a) &&
                a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase),
            RegistryRuleKind.OrphanedUninstallEntry =>
                action == RegistryRepairAction.DeleteKey && valueName is null && TryParent(_locations.UninstallKeys, key, out _, out _),
            _ => false
        };
    }

    private bool IsUnderConfiguredLocation(RegistryKeyRef key) =>
        _locations.StartupKeys.Concat(_locations.AppPathsKeys).Concat(_locations.UninstallKeys)
            .Any(k => k.Hive == key.Hive && k.View == key.View &&
                      (string.Equals(k.Path, key.Path, StringComparison.OrdinalIgnoreCase) ||
                       key.Path.StartsWith(k.Path + "\\", StringComparison.OrdinalIgnoreCase)));

    private static bool ContainsForbiddenSegment(string path) =>
        path.Split('\\').Any(seg => ForbiddenSegments.Contains(seg, StringComparer.OrdinalIgnoreCase));

    private RegistryIssue? EvaluateStartupValue(RegistryKeyRef key, string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        var data = _registry.GetValue(key, name);
        var text = data?.AsText;
        if (data is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var resolution = _resolver.ResolveCommand(text, key.View == RegistryViewKind.Registry32);
        if (resolution.Status != TargetStatus.Missing)
        {
            return null;
        }

        return new RegistryIssue
        {
            Id = MakeId(RegistryRuleKind.StartupEntry, key, name),
            Rule = RegistryRuleKind.StartupEntry,
            RuleTitle = "Invalid startup entry",
            Key = key,
            ValueName = name,
            Reason = "This startup entry launches a program that no longer exists.",
            Evidence = $"The file '{resolution.Path}' was not found on a local fixed drive.",
            Confidence = IssueConfidence.High,
            Risk = RiskLevel.Low,
            Action = RegistryRepairAction.DeleteValue,
            ProposedAction = $"Remove only the startup value '{name}'. The key and all other entries stay. A backup is saved first.",
            ObservedData = data
        };
    }

    private RegistryIssue? EvaluateAppPath(RegistryKeyRef parent, string subKeyName)
    {
        if (!subKeyName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key = parent.Child(subKeyName);
        if (_registry.GetSubKeyNames(key).Count > 0)
        {
            return null;
        }

        var data = _registry.GetValue(key, string.Empty);
        var text = data?.AsText;
        if (data is null || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var resolution = _resolver.ResolvePath(text, parent.View == RegistryViewKind.Registry32);
        if (resolution.Status != TargetStatus.Missing || resolution.Path is null || _resolver.IsUnderWindowsDirectory(resolution.Path))
        {
            return null;
        }

        return new RegistryIssue
        {
            Id = MakeId(RegistryRuleKind.AppPathsEntry, key, null),
            Rule = RegistryRuleKind.AppPathsEntry,
            RuleTitle = "Invalid App Paths registration",
            Key = key,
            ValueName = null,
            Reason = $"The App Paths registration for '{subKeyName}' points to a program that no longer exists.",
            Evidence = $"The file '{resolution.Path}' was not found on a local fixed drive.",
            Confidence = IssueConfidence.High,
            Risk = RiskLevel.Low,
            Action = RegistryRepairAction.DeleteKey,
            ProposedAction = $"Remove only the registration key '{subKeyName}' (it has no sub-keys). Its values are backed up first.",
            ObservedData = data
        };
    }

    private RegistryIssue? EvaluateUninstall(RegistryKeyRef parent, string subKeyName)
    {
        var key = parent.Child(subKeyName);
        if (_registry.GetSubKeyNames(key).Count > 0)
        {
            return null;
        }

        if (IsFlagSet(key, "WindowsInstaller") || IsFlagSet(key, "SystemComponent"))
        {
            return null;
        }

        if (_registry.GetValue(key, "ParentKeyName") is not null || _registry.GetValue(key, "ParentDisplayName") is not null)
        {
            return null;
        }

        var displayName = _registry.GetValue(key, "DisplayName")?.AsText;
        var uninstall = _registry.GetValue(key, "UninstallString");
        var uninstallText = uninstall?.AsText;
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(uninstallText))
        {
            return null;
        }

        if (uninstallText.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolution = _resolver.ResolveCommand(uninstallText, parent.View == RegistryViewKind.Registry32);
        if (resolution.Status != TargetStatus.Missing)
        {
            return null;
        }

        var installLocation = _registry.GetValue(key, "InstallLocation")?.AsText;
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var loc = installLocation.Trim().Trim('"');
            var expanded = _env.Expand(loc);
            if (!CommandTargetResolver.IsDriveAbsolute(expanded) ||
                !_probe.IsOnFixedLocalDrive(expanded) ||
                DirectoryMayExist(expanded, parent.View == RegistryViewKind.Registry32))
            {
                return null;
            }
        }

        var icon = _registry.GetValue(key, "DisplayIcon")?.AsText;
        if (!string.IsNullOrWhiteSpace(icon))
        {
            var iconPath = icon.Trim();
            var comma = iconPath.LastIndexOf(',');
            if (comma > 0 && int.TryParse(iconPath[(comma + 1)..], out _))
            {
                iconPath = iconPath[..comma];
            }

            if (_resolver.ResolvePath(iconPath, parent.View == RegistryViewKind.Registry32).Status != TargetStatus.Missing)
            {
                return null;
            }
        }

        return new RegistryIssue
        {
            Id = MakeId(RegistryRuleKind.OrphanedUninstallEntry, key, null),
            Rule = RegistryRuleKind.OrphanedUninstallEntry,
            RuleTitle = "Orphaned uninstall entry",
            Key = key,
            ValueName = null,
            Reason = $"'{displayName}' is still listed in Apps & features, but its uninstaller (and install folder/icon) no longer exist.",
            Evidence = $"The uninstaller '{resolution.Path}' was not found on a local fixed drive.",
            Confidence = IssueConfidence.High,
            Risk = RiskLevel.Medium,
            Action = RegistryRepairAction.DeleteKey,
            ProposedAction = $"Remove only the list entry '{subKeyName}' (no sub-keys, not a Windows Installer record). Its values are backed up first.",
            ObservedData = uninstall
        };
    }

    private bool DirectoryMayExist(string expanded, bool is32)
    {
        if (_probe.DirectoryExists(expanded))
        {
            return true;
        }

        if (is32 && _env.Get("ProgramFiles") is { Length: > 0 } pf && _env.Get("ProgramFiles(x86)") is { Length: > 0 } pf86 &&
            expanded.StartsWith(pf, StringComparison.OrdinalIgnoreCase))
        {
            return _probe.DirectoryExists(pf86 + expanded[pf.Length..]);
        }

        return false;
    }

    private bool IsFlagSet(RegistryKeyRef key, string name)
    {
        var data = _registry.GetValue(key, name);
        return data is { Kind: RegistryDataKind.DWord } && data.Value is int i && i != 0;
    }

    private static string MakeId(RegistryRuleKind rule, RegistryKeyRef key, string? valueName)
    {
        var text = $"{rule}|{key.Hive}|{key.View}|{key.Path.ToLowerInvariant()}|{valueName?.ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}
