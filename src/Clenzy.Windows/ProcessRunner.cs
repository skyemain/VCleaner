using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Clenzy.Core.Interfaces;

namespace Clenzy.Windows;

/// <summary>
/// Runs a fixed command with no shell. As defense in depth it only launches the two Windows tools Clenzy uses,
/// and only from the Windows directory.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly string[] AllowedNames = { "dism.exe", "powershell.exe" };
    private readonly string _windowsDirectory;

    public ProcessRunner(string windowsDirectory)
    {
        _windowsDirectory = Path.GetFullPath(windowsDirectory);
    }

    public async Task<ProcessResult> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(fileName);
        if (!full.StartsWith(_windowsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !AllowedNames.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to run an executable that is not an approved Windows tool.");
        }

        var info = new ProcessStartInfo(full, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _windowsDirectory
        };

        using var process = new Process { StartInfo = info };
        var output = new StringBuilder();
        void Append(string? line)
        {
            if (line is not null && output.Length < 64 * 1024)
            {
                lock (output)
                {
                    output.AppendLine(line);
                }
            }
        }

        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(-1, ex.Message, false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProcessResult(-1, output.ToString(), true);
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output.ToString(), false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited.
        }
    }
}
