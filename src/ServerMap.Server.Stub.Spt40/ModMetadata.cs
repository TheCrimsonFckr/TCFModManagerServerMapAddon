using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace TCFModManager.ServerMap.Stub;

//
// SPT 4.0.13 declares mod metadata by deriving from AbstractModMetadata - an abstract RECORD whose
// properties are abstract, so every one of them has to be overridden here even where the value is
// null. 4.1 replaced the whole thing with the IModMetadata interface and renamed IsBundleMod to
// HasPrepatcher, which is why this file cannot be shared with the 4.1 stub.
//
public sealed record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.thecrimsonfuckr.servermap";

    public override string Name { get; init; } = "TCFMM Server Map";

    public override string Author { get; init; } = "TheCrimsonFuckr";

    public override List<string>? Contributors { get; init; }

    // SemanticVersioning insists on exactly three parts.
    public override Version Version { get; init; } = new("0.1.0");

    //
    // The 4.0 line only. The range is what stops this build being loaded by a 4.1 server, where its
    // IOnLoad and IHttpListener implementations would not be recognised at all - SPT's own version
    // gate is a better place for that than a MissingMethodException halfway through startup.
    //
    public override Range SptVersion { get; init; } = new(">=4.0.0 <4.1.0");

    // 4.0's name for what 4.1 calls HasPrepatcher. This mod is neither.
    public override bool? IsBundleMod { get; init; } = false;

    public override List<string>? Incompatibilities { get; init; }

    public override Dictionary<string, Range>? ModDependencies { get; init; }

    public override string? Url { get; init; } = "https://github.com/TheCrimsonFckr/TCFModManager";

    public override string License { get; init; } = "MIT";
}
