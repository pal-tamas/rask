using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A property the author declared on a package island by hand.</summary>
internal sealed record UserProp(string ClrName, string WireName);
