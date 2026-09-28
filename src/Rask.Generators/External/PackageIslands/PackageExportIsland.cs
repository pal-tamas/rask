using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>One component a package declaration exports — <c>Button</c> of <c>Mui</c> — as an island.</summary>
/// <param name="Name">The island's type name: the declaration's name and <paramref name="Member" />, <c>MuiButton</c>.</param>
/// <param name="Member">Its entry on the declaration: <c>Mui.Button</c>.</param>
/// <param name="Export">The export it imports, as written in <c>Exports</c>.</param>
internal sealed record PackageExportIsland(string Name, string Member, string Export);
