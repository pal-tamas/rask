using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Writes again the columns of an audit row a save already wrote whose value moved once the database generated the
///     aggregate's key: one <c>UPDATE</c> by the row's key, in the provider's own quoting, whatever type the row is.
/// </summary>
internal static class AuditRowRewrite
{
    /// <summary>Writes <paramref name="completed" />'s values into the row <paramref name="written" /> tracks, where they differ.</summary>
    /// <param name="context">The context whose save wrote the row, inside that save's transaction.</param>
    /// <param name="written">The row as the save wrote it.</param>
    /// <param name="completed">The same row as the format writes it now the generated values are known.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    internal static async Task Write(DbContext context, EntityEntry written, object completed, CancellationToken cancellationToken)
    {
        var type = written.Metadata;
        var key = type.FindPrimaryKey()?.Properties ?? [];
        var moved = type.GetProperties()
            .Where(p => !p.IsShadowProperty() && !p.IsPrimaryKey() && p.ValueGenerated == ValueGenerated.Never)
            .Select(p => (Property: p, Value: p.GetGetter().GetClrValueUsingContainingEntity(completed)))
            .Where(c => !Equals(c.Value, written.Property(c.Property.Name).CurrentValue))
            .ToList();
        if (moved.Count == 0 || key.Count == 0 || type.GetTableName() is not { } table)
        {
            return;
        }

        var sql = context.GetService<ISqlGenerationHelper>();
        var store = StoreObjectIdentifier.Table(table, type.GetSchema());
        var values = new List<object?>(moved.Count + key.Count);
        var statement = new StringBuilder("UPDATE ").Append(sql.DelimitIdentifier(table, type.GetSchema())).Append(" SET ");
        foreach (var (property, value) in moved)
        {
            statement.Append(values.Count == 0 ? "" : ", ").Append(Column(sql, property, store)).Append(" = {").Append(values.Count).Append('}');
            values.Add(Provided(property, value));
        }

        statement.Append(" WHERE ");
        foreach (var property in key)
        {
            statement.Append(values.Count == moved.Count ? "" : " AND ").Append(Column(sql, property, store)).Append(" = {").Append(values.Count).Append('}');
            values.Add(Provided(property, written.Property(property.Name).CurrentValue));
        }

        await context.Database.ExecuteSqlRawAsync(statement.ToString(), values.Cast<object>(), cancellationToken).ConfigureAwait(false);

        // The tracked row says what the table now holds, unchanged.
        foreach (var (property, value) in moved)
        {
            var tracked = written.Property(property.Name);
            tracked.CurrentValue = value;
            tracked.OriginalValue = value;
            tracked.IsModified = false;
        }
    }

    private static string Column(ISqlGenerationHelper sql, IProperty property, StoreObjectIdentifier store) =>
        sql.DelimitIdentifier(property.GetColumnName(store) ?? property.GetColumnName());

    // As the column stores it: a converted value (an enum kept as its name, a value object) through its converter.
    private static object? Provided(IProperty property, object? value) =>
        value is not null && (property.GetValueConverter() ?? property.GetTypeMapping().Converter) is { } converter
            ? converter.ConvertToProvider(value)
            : value;
}
