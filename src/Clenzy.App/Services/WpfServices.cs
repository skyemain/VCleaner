using System.Diagnostics;
using System.IO;
using System.Windows;
using Clenzy.Presentation.Infrastructure;
using Microsoft.Win32;

namespace Clenzy.App.Services;

public sealed class WpfDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

public sealed class WpfDialogService : IDialogService
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText = "Cancel")
    {
        var dialog = new ConfirmDialog(title, message, confirmText, cancelText) { Owner = Owner };
        return Task.FromResult(dialog.ShowDialog() == true);
    }

    public Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ConfirmDialog(title, message, "OK", null) { Owner = Owner };
        dialog.ShowDialog();
        return Task.CompletedTask;
    }

    public string? PickSaveFile(string suggestedName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = suggestedName, Filter = filter, OverwritePrompt = true, AddExtension = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title)
    {
        var dialog = new OpenFileDialog { Title = title, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Owner, "The folder could not be opened: " + ex.Message, "Clenzy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public sealed class AppLifecycle : IAppLifecycle
{
    public string? ExecutablePath => Environment.ProcessPath;

    public void Shutdown() => Application.Current?.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
}

public sealed class LogAccess : ILogAccess
{
    private readonly Clenzy.Core.Services.FileAppLogger _logger;

    public LogAccess(Clenzy.Core.Services.FileAppLogger logger)
    {
        _logger = logger;
    }

    public string LogsDirectory => _logger.LogsDirectory;

    public void ClearLogs() => _logger.ClearLogs();
}
