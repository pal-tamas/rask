using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>What a snapshot resolves to for one island.</summary>
internal sealed record PackageIsland(
    PackageVerdict Verdict,
    string? VerdictDetail,
    EquatableArray<PackageProp> Props,
    EquatableArray<GeneratedType> Types,
    EquatableArray<PropProblem> Problems);
