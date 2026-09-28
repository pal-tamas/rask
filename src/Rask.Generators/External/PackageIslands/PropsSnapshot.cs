using System.Linq;
namespace Rask.Generators.External.PackageIslands;

/// <summary>
///     A committed <c>{Island}.props.json</c>: the props a package component declares in its TypeScript.
/// </summary>
/// <remarks>
///     Equatable all the way down, because it is carried through the incremental pipeline: an unchanged
///     snapshot must compare equal so the outputs built from it are not regenerated on every keystroke.
///     A snapshot that could not be read still arrives, with <see cref="Defect" /> set, so the generator
///     can report where it went wrong instead of silently generating nothing.
/// </remarks>
internal sealed record PropsSnapshot(
    string Path,
    int Schema,
    string Runtime,
    string Module,
    string Export,
    string? PackageName,
    string? PackageVersion,
    string? Tag,
    string Content,
    EquatableArray<SnapshotProp> Props,
    EquatableArray<SnapshotSkip> Skipped,
    EquatableArray<SnapshotNamedType> Types,
    string? Defect,
    int DefectLine,
    int DefectColumn)
{
    /// <summary>The <c>type</c> named <paramref name="name" /> in <see cref="Types" />, or null.</summary>
    public SnapshotType? NamedType(string name) =>
        Types.FirstOrDefault(type => string.Equals(type.Name, name, System.StringComparison.Ordinal))?.Type;
}
