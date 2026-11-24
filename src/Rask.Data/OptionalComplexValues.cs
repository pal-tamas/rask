using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Lets the model hold an optional value object whose members are all optional — <c>Opening? Start</c> over
///     <c>Opening(DateTime? Date, decimal? Quantity)</c> — in its row's own columns: a row with every one of them
///     <c>NULL</c> holds none.
/// </summary>
/// <remarks>
///     <para>
///         EF Core refuses such a property when it validates the model: with no member that is never empty it cannot
///         tell "none" from "one whose members are all empty", and asks for a discriminator column or a JSON column —
///         a column the table does not have. Its materializer already reads it the one way a value object means it
///         (every column <c>NULL</c> → <c>null</c>), its <c>x.Start == null</c> asks every column, and a save of
///         <c>null</c> writes <c>NULL</c> to each; only the refusal stands in the way. Rask's validator on each provider
///         (<c>UseRaskSqlite</c>, <c>UseRaskSqlServer</c>, <c>UseRaskPostgres</c>) lets exactly that refusal pass, for
///         exactly such a property, after every other check EF Core makes of it has run.
///     </para>
///     <para>
///         Linked into Rask.SqlServer, which does not reference Rask.Data; internal in both.
///     </para>
/// </remarks>
internal static class OptionalComplexValues
{
    /// <summary>
    ///     Runs EF Core's checks of <paramref name="complexProperty" /> — <paramref name="validate" />, the provider
    ///     validator's own — and lets its refusal of an optional value whose members are all optional pass.
    /// </summary>
    /// <param name="complexProperty">The complex property being validated.</param>
    /// <param name="validate">The provider validator's <c>ValidatePropertyMapping</c> of it.</param>
    internal static void Validate(IComplexProperty complexProperty, Action validate)
    {
        try
        {
            validate();
        }
        catch (InvalidOperationException refused) when (IsNoneWhenEmpty(complexProperty, refused))
        {
            // The refusal is the last check EF Core makes of a complex property stored in its row's columns; the ones
            // after it are of a JSON column's, which this is not. Every column NULL reads back as none.
        }
    }

    /// <summary>Whether <paramref name="refused" /> is EF Core's refusal of such a property somewhere in <paramref name="model" />.</summary>
    internal static bool Refuses(IModel model, InvalidOperationException refused) =>
        model.GetEntityTypes().Any(entity => Refuses(entity, refused));

    private static bool Refuses(ITypeBase type, InvalidOperationException refused) =>
        type.GetDeclaredComplexProperties().Any(complex => IsNoneWhenEmpty(complex, refused) || Refuses(complex.ComplexType, refused));

    private static bool IsNoneWhenEmpty(IComplexProperty complexProperty, InvalidOperationException refused) =>
        complexProperty is { IsNullable: true, IsCollection: false }
        && !complexProperty.ComplexType.IsMappedToJson()
        && complexProperty.ComplexType.GetFlattenedProperties().Any()
        && complexProperty.ComplexType.GetProperties().All(static member => member.IsNullable)
        && string.Equals(
            refused.Message,
            RelationalStrings.ComplexPropertyOptionalTableSharing(complexProperty.ComplexType.DisplayName(), complexProperty.Name),
            StringComparison.Ordinal);
}
