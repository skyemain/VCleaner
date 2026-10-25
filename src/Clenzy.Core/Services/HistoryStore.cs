using System.Globalization;
using System.Text;
using System.Text.Json;
using Clenzy.Core.Configuration;
using Clenzy.Core.Interfaces;
using Clenzy.Core.Models;

namespace Clenzy.Core.Services;

/// <summary>Stores one JSON file per session, capped to the most recent sessions.</summary>
public sealed class HistoryStore : IHistoryStore
{
    private const int MaxSessions = 100;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly IAppLogger _log;
    private readonly object _gate = new();

    public HistoryStore(AppDataLayout layout, IAppLogger log)
    {
        _directory = layout.HistoryDirectory;
        _log = log;
    }

    public void Save(SessionRecord session)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var stamp = session.ScanTimestamp.UtcDateTime.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
                var path = Path.Combine(_directory, $"session-{stamp}-{session.Id[..Math.Min(8, session.Id.Length)]}.json");
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(session, JsonOptions), new UTF8Encoding(false));
                File.Move(temp, path, overwrite: true);
                Prune();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning($"Could not save session history: {ex.Message}");
            }
        }
    }

    public IReadOnlyList<SessionRecord> GetRecent(int count)
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                return Array.Empty<SessionRecord>();
            }

            var result = new List<SessionRecord>();
            foreach (var file in Directory.EnumerateFiles(_directory, "session-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Take(count))
            {
                var record = TryRead(file);
                if (record is not null)
                {
                    result.Add(record);
                }
            }

            return result;
        }
    }

    public SessionRecord? GetLastCleanup()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                return null;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, "session-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Take(MaxSessions))
            {
                var record = TryRead(file);
                if (record?.CleanupTimestamp is not null && !record.WasCanceled)
                {
                    return record;
                }
            }

            return null;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, "session-*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _log.Warning($"Could not remove history file: {ex.Message}");
                }
            }
        }
    }

    private SessionRecord? TryRead(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);
            return JsonSerializer.Deserialize<SessionRecord>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Ignoring unreadable history file '{Path.GetFileName(file)}'.");
            return null;
        }
    }

    private void Prune()
    {
        var files = Directory.EnumerateFiles(_directory, "session-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(MaxSessions);
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore.
            }
        }
    }
}
