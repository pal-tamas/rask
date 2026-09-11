namespace Rask.Generators.External.PackageIslands;

/// <summary>One literal of an enum-like union: a string, a number or a boolean, as written.</summary>
internal sealed record SnapshotLiteral(string Text, bool IsNumber, bool IsBoolean);

/// <summary>A member of an object type in a props snapshot.</summary>
internal sealed record SnapshotMember(string Name, bool Required, string? Doc, SnapshotType Type);

/// <summary>A parameter of a callback prop.</summary>
internal sealed record SnapshotArg(string Name, bool Optional, SnapshotType Type);

/// <summary>A named object type a snapshot declares once and refers to by <c>ref</c>.</summary>
internal sealed record SnapshotNamedType(string Name, SnapshotType Type);

/// <summary>A prop the extractor saw and deliberately did not describe.</summary>
internal sealed record SnapshotSkip(string Name, string Reason, string? Detail);

/// <summary>
///     The type of a prop, as the extractor described it.
/// </summary>
/// <param name="Kind">
///     <c>string</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>enum</c>, <c>union</c>, <c>array</c>,
///     <c>record</c>, <c>object</c>, <c>ref</c>, <c>callback</c>, <c>event</c>, <c>unknown</c> — or any other
///     text a newer extractor wrote, which the resolver reports rather than guesses at.
/// </param>
/// <param name="Nullable">Whether <c>null</c> is a legal value.</param>
/// <param name="Values">An enum's literals.</param>
/// <param name="Open">Whether an enum also accepts other strings (<c>'a' | (string &amp; {})</c>).</param>
/// <param name="Element">An array's element, or a record's value.</param>
/// <param name="Of">A type union's alternatives.</param>
/// <param name="Members">An inline object's members.</param>
/// <param name="Name">A <c>ref</c>'s target, or an event's DOM type name.</param>
/// <param name="Args">A callback's parameters.</param>
/// <param name="Returns">Whether a callback returns something other than <c>void</c>.</param>
internal sealed record SnapshotType(
    string Kind,
    bool Nullable,
    EquatableArray<SnapshotLiteral> Values,
    bool Open,
    SnapshotType? Element,
    EquatableArray<SnapshotType> Of,
    EquatableArray<SnapshotMember> Members,
    string? Name,
    EquatableArray<SnapshotArg> Args,
    bool Returns);

/// <summary>One prop of a package component.</summary>
/// <param name="Name">The prop's name in the package — the JSON key, unless <paramref name="Wire" /> differs.</param>
/// <param name="Wire">The key the props JSON carries (an event binding for Lit and Angular).</param>
/// <param name="Required">Whether the package requires it.</param>
/// <param name="Doc">The package's own documentation for it.</param>
/// <param name="Default">The package's documented default, as source text.</param>
/// <param name="Type">Its type.</param>
/// <param name="Line">Where it is declared in the snapshot, for diagnostics.</param>
/// <param name="Column">Where it is declared in the snapshot, for diagnostics.</param>
internal sealed record SnapshotProp(
    string Name,
    string Wire,
    bool Required,
    string? Doc,
    string? Default,
    SnapshotType Type,
    int Line,
    int Column);

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
    public SnapshotType? NamedType(string name)
    {
        foreach (var type in Types)
        {
            if (string.Equals(type.Name, name, System.StringComparison.Ordinal))
            {
                return type.Type;
            }
        }

        return null;
    }
}
