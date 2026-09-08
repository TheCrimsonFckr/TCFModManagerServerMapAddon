using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace TCFModManager.ServerMap.Stub;

//
// SPT 4.1 declares mod metadata through IModMetadata. This is NOT the 4.0 AbstractModMetadata
// record - that type no longer exists, and IsBundleMod became HasPrepatcher.
//
public sealed record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.thecrimsonfuckr.servermap";

    public string Name { get; init; } = "TCFMM Server Map";

    public string Author { get; init; } = "TheCrimsonFuckr";

    public List<string>? Contributors { get; init; }

    // SemanticVersioning insists on exactly three parts.
    public Version Version { get; init; } = new("0.1.0");

    // Deliberately the whole 4.1 line while this is a spike, not a released range.
    public Range SptVersion { get; init; } = new(">=4.1.0 <4.2.0");

    public bool HasPrepatcher { get; init; }

    public List<string>? Incompatibilities { get; init; }

    public Dictionary<string, Range>? ModDependencies { get; init; }

    public string? Url { get; init; } = "https://github.com/TheCrimsonFckr/TCFModManager";

    public string License { get; init; } = "MIT";
}
