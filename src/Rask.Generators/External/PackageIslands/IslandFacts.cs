using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>
///     What both generators need to know about an island class to pair it with a snapshot. Symbol-free,
///     so it can ride on the factory generator's cached candidate.
/// </summary>
/// <param name="Name">The class's simple name — the snapshot is <c>{Name}.props.json</c>.</param>
/// <param name="Namespace">Its namespace, or null for the global namespace.</param>
/// <param name="Runtime">The runtime its base class declares.</param>
/// <param name="Module">Its constant <c>Module</c> override, or null when it declares none.</param>
/// <param name="Export">
///     The package export it mounts — its constant <c>Export</c> override, or <c>default</c> when it declares none.
/// </param>
/// <param name="IsPublic">Whether generated types beside it must be public to match it.</param>
/// <param name="Directories">The normalised directories its declarations live in.</param>
/// <param name="UserProps">The props declared on the class itself.</param>
/// <param name="Reserved">
///     Names a generated prop must not take: Rask's own members, every member the class declares itself, and
///     the class's name.
/// </param>
/// <param name="TypeNames">
///     The types already declared in the island's namespace. A generated enum or record lands in that namespace,
///     so it must not take one of these names.
/// </param>
internal sealed record IslandFacts(
    string Name,
    string? Namespace,
    string Runtime,
    string? Module,
    string Export,
    bool IsPublic,
    EquatableArray<string> Directories,
    EquatableArray<UserProp> UserProps,
    EquatableArray<string> Reserved,
    EquatableArray<string> TypeNames)
{
    /// <summary>Whether <see cref="Module" /> names a package rather than a file beside the class.</summary>
    public bool IsPackage => Module is not null && PackageSpecifier.IsBare(Module);
}
