using System.Text.Json;
namespace Clenzy.Core.Windows11;

public sealed record MaintenanceEntry
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string Impact { get; init; } = "";
    public string Profile { get; init; } = "Safe";
    public string Kind { get; init; } = "Registry";
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public int Desired { get; init; }
    public bool Machine { get; init; }
    public bool AllowCreate { get; init; }
    public bool Policy { get; init; }
    public bool Destructive { get; init; }
    public int MinBuild { get; init; } = 22000;
    public int MinRevision { get; init; }
    public OptimizationAction Action => new(Id, Category, Title, Impact, Enum.Parse<OptimizationProfile>(Profile), Kind == "Package");
    public OptimizationState DesiredState => Kind switch {
        "Package" => new(false, null, "Package"),
        "Startup" => new(false, null, "String"),
        "Service" => new(true, "Manual", "Service"),
        _ => new(true, Desired.ToString(), "DWord")
    };
}
public static class MaintenanceCatalog
{
    public static readonly string Json = Read();
    public static readonly IReadOnlyList<MaintenanceEntry> Entries = JsonSerializer.Deserialize<MaintenanceEntry[]>(Json) ?? throw new InvalidDataException("Missing maintenance catalog");
    public static MaintenanceEntry Get(string id) => Entries.SingleOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Unknown maintenance action");
    private static string Read() {
        using var stream = typeof(MaintenanceCatalog).Assembly.GetManifestResourceStream("Clenzy.Windows11.Catalog.json") ?? throw new InvalidDataException("Missing catalog");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
