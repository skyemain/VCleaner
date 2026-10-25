using System.Runtime.InteropServices;
using Clenzy.Core.Models;

namespace Clenzy.Windows;

/// <summary>Resolves the real locations the cleaners work against. Nothing here touches the disk.</summary>
public static class WindowsSystemPaths
{
    private static readonly Guid DownloadsFolder = new("374DE290-123F-4565-9164-39C4925E467B");

    public static SystemPaths Create()
    {
        string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        var windows = Folder(Environment.SpecialFolder.Windows);
        var profile = Folder(Environment.SpecialFolder.UserProfile);
        var temp = Path.GetFullPath(Path.GetTempPath());
        var tmpVar = Environment.GetEnvironmentVariable("TMP");

        return new SystemPaths
        {
            SystemDriveRoot = Path.GetPathRoot(windows) ?? @"C:\",
            UserProfile = profile,
            LocalAppData = Folder(Environment.SpecialFolder.LocalApplicationData),
            RoamingAppData = Folder(Environment.SpecialFolder.ApplicationData),
            ProgramData = Folder(Environment.SpecialFolder.CommonApplicationData),
            WindowsDirectory = windows,
            SystemDirectory = Folder(Environment.SpecialFolder.System),
            ProgramFiles = Folder(Environment.SpecialFolder.ProgramFiles),
            ProgramFilesX86 = Folder(Environment.SpecialFolder.ProgramFilesX86),
            UserTemp = temp,
            UserTmp = string.IsNullOrWhiteSpace(tmpVar) ? null : tmpVar,
            Documents = Folder(Environment.SpecialFolder.MyDocuments),
            Desktop = Folder(Environment.SpecialFolder.DesktopDirectory),
            Downloads = KnownDownloads(profile),
            Pictures = Folder(Environment.SpecialFolder.MyPictures),
            Videos = Folder(Environment.SpecialFolder.MyVideos),
            Music = Folder(Environment.SpecialFolder.MyMusic),
            ExtraProtectedRoots = CloudRoots()
        };
    }

    private static string KnownDownloads(string profile)
    {
        var id = DownloadsFolder;
        try
        {
            if (NativeMethods.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var ptr) == 0 && ptr != IntPtr.Zero)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
                finally
                {
                    NativeMethods.CoTaskMemFree(ptr);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            // Fall through to the conventional location.
        }

        return Path.Combine(profile, "Downloads");
    }

    private static IReadOnlyList<string> CloudRoots()
    {
        var roots = new List<string>();
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value))
            {
                roots.Add(value);
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
