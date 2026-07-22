using SPTarkov.Server.Core.Models.Spt.Mod;

namespace TarkovPerformanceSuite.Server;

/// <summary>
/// Describes the optional server-side loading component to the SPT mod loader.
/// </summary>
public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.lucaswilluweit.tarkovperformancesuite.loadingserver";
    public override string Name { get; init; } = "Tarkov Performance Suite - Loading Server";
    public override string Author { get; init; } = "Lucas Willuweit";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
