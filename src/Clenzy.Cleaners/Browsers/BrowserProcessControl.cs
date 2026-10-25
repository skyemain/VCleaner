using System.Diagnostics;
using Clenzy.Core.Configuration;
using Clenzy.Core.Models;

namespace Clenzy.Cleaners.Browsers;

public sealed record BrowserCloseResult(int ProcessesFound, int ProcessesRemaining, bool ForcedAny)
{
    public static BrowserCloseResult NotRunning { get; } = new(0, 0, false);

    public bool IsClosed => ProcessesRemaining == 0;
}

/// <summary>Closes a running Chromium browser so its caches can be cleaned. Never touches the browser's profile data.</summary>
public interface IBrowserCloser
{
    BrowserCloseResult Close(BrowserDescriptor browser, CancellationToken cancellationToken);
}

/// <summary>Names the browsers that would be closed for the current settings, so the UI can ask before anything is closed.</summary>
public interface IBrowserInventory
{
    IReadOnlyList<string> GetBrowsersThatWouldBeClosed(ClenzySettings settings);
}

public sealed class BrowserInventory : IBrowserInventory
{
    private readonly SystemPaths _paths;
    private readonly IBrowserRunningDetector _detector;

    public BrowserInventory(SystemPaths paths, IBrowserRunningDetector detector)
    {
        _paths = paths;
        _detector = detector;
    }

    public IReadOnlyList<string> GetBrowsersThatWouldBeClosed(ClenzySettings settings)
    {
        var names = new List<string>();
        if (!settings.CloseBrowsersBeforeCleaning)
        {
            return names;
        }

        Check(BrowserCatalog.Chrome(_paths), settings.CleanChromeCache || settings.ClearBrowserSiteStorage);
        Check(BrowserCatalog.Edge(_paths), settings.CleanEdgeCache || settings.ClearBrowserSiteStorage);
        return names;

        void Check(BrowserDescriptor browser, bool enabled)
        {
            var userData = Path.Combine(_paths.LocalAppData, browser.UserDataRelativePath);
            if (enabled && Directory.Exists(userData) && _detector.IsRunning(browser, userData))
            {
                names.Add(browser.DisplayName);
            }
        }
    }
}

/// <summary>
/// Closes the browser in two steps: first the way a user would (closing each window, so the browser saves its state and
/// sign-in cookies normally), then, if it is still running after the grace period, by ending the remaining processes.
/// Only processes of the current Windows session whose executable lives in the browser's own install folder are touched.
/// </summary>
public sealed class ProcessBrowserCloser : IBrowserCloser
{
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(800);

    public BrowserCloseResult Close(BrowserDescriptor browser, CancellationToken cancellationToken)
    {
        var targets = FindTargets(browser);
        if (targets.Count == 0)
        {
            return BrowserCloseResult.NotRunning;
        }

        var found = targets.Count;
        var forced = false;
        try
        {
            var deadline = DateTime.UtcNow + GracePeriod;
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                var alive = false;
                foreach (var process in targets)
                {
                    if (HasExited(process))
                    {
                        continue;
                    }

                    alive = true;
                    RequestClose(process);
                }

                if (!alive)
                {
                    break;
                }

                Thread.Sleep(200);
            }

            foreach (var process in targets)
            {
                if (HasExited(process))
                {
                    continue;
                }

                forced = true;
                try
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Already gone, or not ours to end (for example an elevated process).
                }
            }

            Thread.Sleep(SettleTime);
            return new BrowserCloseResult(found, targets.Count(p => !HasExited(p)), forced);
        }
        finally
        {
            foreach (var process in targets)
            {
                process.Dispose();
            }
        }
    }

    private static List<Process> FindTargets(BrowserDescriptor browser)
    {
        var result = new List<Process>();
        int currentSession;
        try
        {
            using var self = Process.GetCurrentProcess();
            currentSession = self.SessionId;
        }
        catch (InvalidOperationException)
        {
            return result;
        }

        foreach (var name in browser.ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                var keep = false;
                try
                {
                    var path = process.MainModule?.FileName;
                    keep = path is not null &&
                           process.SessionId == currentSession &&
                           path.Replace('/', '\\').Contains(browser.ExecutablePathFragment, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Cannot be inspected (for example elevated); it is left alone.
                }

                if (keep)
                {
                    result.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
        }

        return result;
    }

    private static void RequestClose(Process process)
    {
        try
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                process.CloseMainWindow();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // The process exited between the check and the request.
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true;
        }
    }
}
