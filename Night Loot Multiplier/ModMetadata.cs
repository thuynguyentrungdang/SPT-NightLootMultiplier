using SPTarkov.Server.Core.Models.Spt.Mod;

namespace NightLootMultiplier;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.kobethuy.nightlootmultiplier";
    public string Name { get; init; } = "NightLootMultiplier";
    public string Author { get; init; } = "kobethuy";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "";
    public bool HasPrepatcher { get; init; }
    public string License { get; init; } = "MIT";
}
