namespace Clenzy.Core.Configuration;

/// <summary>Locations of Clenzy's own data (settings, logs, history, registry backups).</summary>
public sealed class AppDataLayout
{
    public AppDataLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LogsDirectory => Path.Combine(Root, "logs");
    public string HistoryDirectory => Path.Combine(Root, "history");
    public string RegistryBackupsDirectory => Path.Combine(Root, "registry-backups");

    public static AppDataLayout ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clenzy"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(HistoryDirectory);
        Directory.CreateDirectory(RegistryBackupsDirectory);
    }
}
