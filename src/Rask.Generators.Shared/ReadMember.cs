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
internal sealed record ReadMember(
    string Name,
    string ColumnName,
    string Path,
    string TypeName,
    bool Nullable,
    bool IsReferenceType);
