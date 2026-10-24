using System.Text.Json;
using CleanSlate.Core.Interfaces;

namespace CleanSlate.Core.Configuration;

/// <summary>Loads and atomically saves settings. A corrupt file is preserved as <c>.corrupt</c> and defaults are used.</summary>
public sealed class SettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly AppDataLayout _layout;
    private readonly IAppLogger _log;
    private readonly object _gate = new();

    public SettingsStore(AppDataLayout layout, IAppLogger log)
    {
        _layout = layout;
        _log = log;
    }

    public CleanSlateSettings Load()
    {
        lock (_gate)
        {
            var path = _layout.SettingsFile;
            if (!File.Exists(path))
            {
                return new CleanSlateSettings();
            }

            try
            {
                using var stream = File.OpenRead(path);
                var loaded = JsonSerializer.Deserialize<CleanSlateSettings>(stream, JsonOptions) ?? new CleanSlateSettings();
                loaded.Normalize();
                return loaded;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _log.Warning($"Settings file could not be read and defaults will be used: {ex.Message}");
                try
                {
                    File.Move(path, path + ".corrupt", overwrite: true);
                }
                catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                {
                    _log.Warning($"Could not preserve corrupt settings file: {moveEx.Message}");
                }

                return new CleanSlateSettings();
            }
        }
    }

    public void Save(CleanSlateSettings settings)
    {
        lock (_gate)
        {
            var copy = settings.Clone();
            copy.Normalize();
            Directory.CreateDirectory(_layout.Root);
            var temp = _layout.SettingsFile + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, copy, JsonOptions);
                stream.Flush(true);
            }

            File.Move(temp, _layout.SettingsFile, overwrite: true);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Save(new CleanSlateSettings());
        }
    }
}
