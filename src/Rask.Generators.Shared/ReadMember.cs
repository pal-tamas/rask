namespace Rask.Generators.Shared;

/// <summary>One column on a read face: a primitive, named for C# and mapped to the column the write model uses.</summary>
/// <param name="Name">The member's name — <c>TotalAmount</c>.</param>
/// <param name="ColumnName">The column it maps to — <c>Total_Amount</c>, which the write model already owns.</param>
/// <param name="Path">
///     Where it lives on the write model — <c>Total.Amount</c> — so the runtime can find the property EF
///     actually built and copy what it decided.
/// </param>
/// <param name="TypeName">Its fully-qualified type, without a nullable annotation.</param>
/// <param name="Nullable">Whether it is declared nullable.</param>
/// <param name="IsReferenceType">
///     Whether the type is a class, so a non-nullable one needs an initializer the compiler accepts.
/// </param>
/// <param name="Shown">
///     The <c>[Display]</c>, <c>[DisplayFormat]</c> and <c>[ScaffoldColumn]</c> of the property it reads, written out, or
///     null: how a list names, writes and shows the column.
/// </param>
/// <param name="Shape">
///     The C# of its field shape — its value object's <c>__Field</c> and what the property's attributes add — or null
///     for one whose type says all there is.
/// </param>
/// <param name="AsKey">
///     The same column in the type the property declares — <c>ProductId Product</c> — for a one-value type that may be
///     another entity's key, or null. Which one the face takes is settled once every entity is known
///     (<see cref="ReadModelShape.KeepKeysTyped" />): an entity's key type is a converted column on the write model, so
///     the face keeps it as declared; any other one-value type is the value it holds.
/// </param>
internal sealed record ReadMember(
    string Name,
    string ColumnName,
    string Path,
    string TypeName,
    bool Nullable,
    bool IsReferenceType,
    string? Shown = null,
    string? Shape = null,
    ReadMember? AsKey = null);
