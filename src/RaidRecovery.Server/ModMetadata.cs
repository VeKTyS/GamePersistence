using SPTarkov.Server.Core.Models.Spt.Mod;

namespace RaidRecovery.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.oceane.raidrecovery";
    public string Name { get; init; } = "RaidRecovery";
    public string Author { get; init; } = "VeKTyS";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.11.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}
