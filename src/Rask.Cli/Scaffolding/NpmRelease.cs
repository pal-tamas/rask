using Semver;

namespace Rask.Cli.Scaffolding;

/// <summary>One installable release of an npm package: its version, the peer ranges it declares, and the Node it runs on.</summary>
internal sealed record NpmRelease(SemVersion Version, IReadOnlyDictionary<string, string> Peers, string? Node = null);
