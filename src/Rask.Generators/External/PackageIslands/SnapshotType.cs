namespace Rask.Generators.External.PackageIslands;

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
