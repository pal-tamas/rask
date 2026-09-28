using System.Data;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>One mapped column of a <see cref="BulkInsertPlan"/>.</summary>
/// <param name="ColumnName">The mapped column, undelimited.</param>
/// <param name="ParameterName">The name the <c>DbParameter</c> is bound under.</param>
/// <param name="ParameterPlaceholder">How the statement text refers to that parameter.</param>
/// <param name="Read">Reads the property off an entity.</param>
/// <param name="Converter">The value converter the provider stores through, when there is one.</param>
/// <param name="DbType">The parameter's <see cref="System.Data.DbType"/>, when the mapping names one.</param>
/// <param name="GeneratedName">The <c>Entity.Property</c> name of a value-generated column, for the error.</param>
/// <param name="ClrDefault">The CLR default a value-generated column must not still hold.</param>
internal sealed record BulkInsertColumn(
    string ColumnName,
    string ParameterName,
    string ParameterPlaceholder,
    Func<object, object?> Read,
    ValueConverter? Converter,
    DbType? DbType,
    string? GeneratedName,
    object? ClrDefault)
{
    /// <summary>Reads the column off <paramref name="entity"/> in the form the provider stores.</summary>
    internal object? ValueFor(object entity)
    {
        var value = Read(entity);

        // EF would have filled a value-generated property before the insert, and an entity assigns its own Guid
        // key; this path does neither, so an unset one must be reported rather than written as a default that
        // collides on the next row.
        if (GeneratedName is not null && Equals(value, ClrDefault))
        {
            throw BulkInsertPlan.Unsupported(
                $"{GeneratedName} is still unset, and nothing generates or assigns values on this path. " +
                "Assign it before inserting.");
        }

        if (value is null || Converter is null)
        {
            return value;
        }

        return Converter.ConvertToProvider(value);
    }
}
