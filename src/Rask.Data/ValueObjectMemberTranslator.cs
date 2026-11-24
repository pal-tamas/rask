using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Reads the one value a value object is stored as straight from its column in a query:
///     <c>Where(t =&gt; t.Quantity.Value &gt; 0)</c>, <c>Sum(t =&gt; t.Quantity.Value)</c>, <c>OrderBy(f =&gt; f.Moment.Value)</c>.
/// </summary>
/// <remarks>
///     <para>
///         A column mapped through Rask's own converter (<see cref="ValueObjectColumn" />) already holds the value, so
///         <c>.Value</c> is that column, typed as the value — with the store type the column has, so a <c>decimal(9,2)</c>
///         stays one, and no <c>CAST</c> is written. A column of a converter of the app's own is left alone: what it
///         stores need not be the value, and EF Core then says it cannot translate the member.
///     </para>
///     <para>Provider-neutral: SQL Server, SQLite and PostgreSQL all read the column the same way.</para>
/// </remarks>
/// <param name="typeMappings">The provider's type mappings.</param>
internal sealed class ValueObjectMemberTranslator(IRelationalTypeMappingSource typeMappings) : IMemberTranslatorPlugin, IMemberTranslator
{
    public IEnumerable<IMemberTranslator> Translators => [this];

    public SqlExpression? Translate(
        SqlExpression? instance,
        MemberInfo member,
        Type returnType,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (instance is not ColumnExpression { TypeMapping: { } stored } column || OwnOf(column, stored, member) is not { } own)
        {
            return null;
        }

        // The value, stored the way the column stores it: a wall-clock moment as UTC where the provider keeps only UTC.
        var held = Nullable.GetUnderlyingType(returnType) ?? returnType;
        var mapping = typeMappings.FindMapping(held, stored.StoreType) ?? typeMappings.FindMapping(held);
        if (mapping is not null && OneValueConverters.StoredAs(own) is { } through)
        {
            mapping = (RelationalTypeMapping)mapping.WithComposedConverter(through, null, null, null, null);
        }

        return mapping is null
            ? null
            : new ColumnExpression(column.Name, column.TableAlias, column.Column, returnType, mapping, column.IsNullable);
    }

    // Rask's own converter of the column, which reads `member`: the type mapping's, unless the provider composed one of
    // its own after it (an enum held as a value object is stored through EF Core's enum-to-number), then its property's.
    private static ValueConverter? OwnOf(ColumnExpression column, RelationalTypeMapping stored, MemberInfo member)
    {
        if (OneValueConverters.StoresAs(stored.Converter, member))
        {
            return stored.Converter;
        }

        return column.Column?.PropertyMappings.Select(m => m.Property.GetValueConverter())
            .FirstOrDefault(c => OneValueConverters.StoresAs(c, member));
    }
}
