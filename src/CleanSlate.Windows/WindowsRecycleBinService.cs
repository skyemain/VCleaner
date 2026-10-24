using System.Runtime.InteropServices;
using CleanSlate.Core.Interfaces;

namespace CleanSlate.Windows;

/// <summary>Queries and empties the Recycle Bin through the shell. Emptying is permanent.</summary>
public sealed class WindowsRecycleBinService : IRecycleBinService
{
    private const int EUnexpected = unchecked((int)0x8000FFFF);

    public bool IsSupported => OperatingSystem.IsWindows();

    public RecycleBinInfo Query()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        var hr = NativeMethods.SHQueryRecycleBinW(null, ref info);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return new RecycleBinInfo(Math.Max(0, info.i64Size), Math.Max(0, info.i64NumItems));
    }

    public void Empty()
    {
        const uint flags = NativeMethods.SHERB_NOCONFIRMATION | NativeMethods.SHERB_NOPROGRESSUI | NativeMethods.SHERB_NOSOUND;
        var hr = NativeMethods.SHEmptyRecycleBinW(IntPtr.Zero, null, flags);
        if (hr != 0 && hr != EUnexpected)
        {
            // E_UNEXPECTED is what the shell returns when the bin is already empty.
            Marshal.ThrowExceptionForHR(hr);
        }
    }
}
