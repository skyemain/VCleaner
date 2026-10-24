using System.ComponentModel;
using System.Diagnostics;
using CleanSlate.Core.Interfaces;

namespace CleanSlate.Windows;

/// <summary>
/// Asks Windows to trim the working set of each running program (the documented <c>EmptyWorkingSet</c> call). The pages move to
/// the standby list and are reloaded on demand, so nothing is closed or lost, but the freed RAM is partly temporary and a
/// trimmed app can feel briefly slower when it is used next. Only processes this user may open are trimmed.
/// </summary>
public sealed class WindowsMemoryCleaner : IMemoryCleaner
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(700);

    public MemoryStatus GetStatus()
    {
        var info = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (!NativeMethods.GlobalMemoryStatusEx(ref info))
        {
            throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }

        return new MemoryStatus((long)info.ullTotalPhys, (long)info.ullAvailPhys);
    }

    public MemoryCleanResult Clean(CancellationToken cancellationToken)
    {
        var before = GetStatus();
        var trimmed = 0;
        var skipped = 0;

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new MemoryCleanResult(before, before, 0, 0);
        }

        foreach (var process in processes)
        {
            using (process)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (process.Id <= 4)
                {
                    continue;
                }

                if (TryTrim(process.Id))
                {
                    trimmed++;
                }
                else
                {
                    skipped++;
                }
            }
        }

        Thread.Sleep(SettleTime);
        return new MemoryCleanResult(before, GetStatus(), trimmed, skipped);
    }

    private static bool TryTrim(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryInformation | NativeMethods.ProcessSetQuota, false, processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.EmptyWorkingSet(handle);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
