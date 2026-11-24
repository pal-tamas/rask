using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

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
        if (instance is not ColumnExpression { TypeMapping: { } stored } column || !StoresAs(column, stored, member))
        {
            return null;
        }

        var held = Nullable.GetUnderlyingType(returnType) ?? returnType;
        var mapping = typeMappings.FindMapping(held, stored.StoreType) ?? typeMappings.FindMapping(held);
        return mapping is null
            ? null
            : new ColumnExpression(column.Name, column.TableAlias, column.Column, returnType, mapping, column.IsNullable);
    }

    // The column's own property says how it is stored; the type mapping says it too, unless the provider composed a
    // converter of its own after Rask's — an enum held as a value object is stored through EF Core's enum-to-number.
    private static bool StoresAs(ColumnExpression column, RelationalTypeMapping stored, MemberInfo member)
    {
        if (OneValueConverters.StoresAs(stored.Converter, member))
        {
            return true;
        }

        return column.Column?.PropertyMappings.Any(m => OneValueConverters.StoresAs(m.Property.GetValueConverter(), member)) == true;
    }
}
