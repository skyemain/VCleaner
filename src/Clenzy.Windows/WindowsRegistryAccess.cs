using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;
using Microsoft.Win32;

namespace Clenzy.Windows;

/// <summary>Registry access through Microsoft.Win32. Values are always read raw (environment variables unexpanded).</summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    private static RegistryKey? OpenBase(RegistryKeyRef key) =>
        RegistryKey.OpenBaseKey(
            key.Hive == RegistryHiveKind.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine,
            key.View == RegistryViewKind.Registry32 ? RegistryView.Registry32 : RegistryView.Registry64);

    private static RegistryKey? Open(RegistryKeyRef key, bool writable)
    {
        using var root = OpenBase(key);
        return root?.OpenSubKey(key.Path, writable);
    }

    public bool KeyExists(RegistryKeyRef key)
    {
        try
        {
            using var k = Open(key, false);
            return k is not null;
        }
        catch (Exception ex) when (IsAccessProblem(ex))
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetSubKeyNames(RegistryKeyRef key)
    {
        using var k = Open(key, false);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetValueNames(RegistryKeyRef key)
    {
        using var k = Open(key, false);
        return k?.GetValueNames() ?? Array.Empty<string>();
    }

    public RegistryData? GetValue(RegistryKeyRef key, string name)
    {
        using var k = Open(key, false);
        if (k is null)
        {
            return null;
        }

        RegistryValueKind kind;
        object? value;
        try
        {
            kind = k.GetValueKind(name);
            value = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }

        return (kind, value) switch
        {
            (RegistryValueKind.String, string s) => new RegistryData(RegistryDataKind.String, s),
            (RegistryValueKind.ExpandString, string s) => new RegistryData(RegistryDataKind.ExpandString, s),
            (RegistryValueKind.MultiString, string[] m) => new RegistryData(RegistryDataKind.MultiString, m),
            (RegistryValueKind.DWord, int i) => new RegistryData(RegistryDataKind.DWord, i),
            (RegistryValueKind.QWord, long l) => new RegistryData(RegistryDataKind.QWord, l),
            (RegistryValueKind.Binary, byte[] b) => new RegistryData(RegistryDataKind.Binary, b),
            _ => null
        };
    }

    public void SetValue(RegistryKeyRef key, string name, RegistryData data)
    {
        using var k = Open(key, true) ?? throw new IOException("The registry key does not exist.");
        var kind = data.Kind switch
        {
            RegistryDataKind.String => RegistryValueKind.String,
            RegistryDataKind.ExpandString => RegistryValueKind.ExpandString,
            RegistryDataKind.MultiString => RegistryValueKind.MultiString,
            RegistryDataKind.DWord => RegistryValueKind.DWord,
            RegistryDataKind.QWord => RegistryValueKind.QWord,
            _ => RegistryValueKind.Binary
        };
        k.SetValue(name, data.Value, kind);
    }

    public void CreateKey(RegistryKeyRef key)
    {
        using var root = OpenBase(key) ?? throw new IOException("The registry hive could not be opened.");
        using var created = root.CreateSubKey(key.Path, writable: true);
        if (created is null)
        {
            throw new IOException("The registry key could not be created.");
        }
    }

    public void DeleteValue(RegistryKeyRef key, string name)
    {
        using var k = Open(key, true) ?? throw new IOException("The registry key does not exist.");
        k.DeleteValue(name, throwOnMissingValue: true);
    }

    public void DeleteKey(RegistryKeyRef key)
    {
        var separator = key.Path.LastIndexOf('\\');
        if (separator <= 0)
        {
            throw new InvalidOperationException("Top-level keys are never deleted.");
        }

        var parent = key with { Path = key.Path[..separator] };
        using var k = Open(parent, true) ?? throw new IOException("The parent registry key does not exist.");
        k.DeleteSubKey(key.Path[(separator + 1)..], throwOnMissingSubKey: true);
    }

    private static bool IsAccessProblem(Exception ex) =>
        ex is System.Security.SecurityException or UnauthorizedAccessException or IOException;
}
