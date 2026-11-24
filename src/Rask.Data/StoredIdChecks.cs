using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Every id kept as text — a key or a reference whose column is text, a typed id over a <see cref="Guid" /> the way
///     an existing schema keeps it — read as stored and asked whether the app reads it, and whether it writes it the same
///     way: what <c>rask audit</c> asks before any row is loaded, because a row whose id cannot be read cannot be loaded.
/// </summary>
/// <remarks>
///     <para>
///         The column is read as the text it holds, through the provider's own quoting and no model, so an id that is no
///         <see cref="Guid" /> at all is found rather than failing the read. It is turned into the id by the column's own
///         converter and back: one that does not turn is unreadable, one that comes back spelled otherwise — upper case,
///         braces — is not as the app writes it, which matters where text is compared or ordered.
///     </para>
///     <para>Every row is read, a deleted one or another tenant's included: the column is what is asked about.</para>
/// </remarks>
internal static class StoredIdChecks
{
    /// <summary>What an id is told that its type cannot read.</summary>
    internal const string Unreadable = "Not an id its type can read.";

    /// <summary>What an id is told that the app reads but would write otherwise.</summary>
    internal const string NotAsWritten = "Not written the way the app writes it (letter case or format differs).";

    /// <summary>The text ids of <paramref name="types" /> that break either rule, each under its type and property.</summary>
    /// <param name="context">The context whose connection is read.</param>
    /// <param name="types">The entity types to ask about.</param>
    /// <param name="breaches">Where each finding is recorded.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    internal static async Task Check(
        DbContext context, IEnumerable<IEntityType> types, RuleAuditFindings breaches, CancellationToken cancellationToken)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var asked = new HashSet<(string?, string, string)>();
        foreach (var type in types)
        {
            if (type.GetTableName() is not { } table)
            {
                continue;
            }

            var store = StoreObjectIdentifier.Table(table, type.GetSchema());
            foreach (var property in type.GetProperties().Where(static p => p.IsKey() || p.IsForeignKey()))
            {
                if (TextConverter(property) is not { } converter ||
                    property.GetColumnName(store) is not { } column ||
                    !asked.Add((type.GetSchema(), table, column)))
                {
                    continue;
                }

                var text = $"SELECT {sql.DelimitIdentifier(column)} FROM {sql.DelimitIdentifier(table, type.GetSchema())} "
                           + $"WHERE {sql.DelimitIdentifier(column)} IS NOT NULL";
                await foreach (var stored in StoredText.Read(context, text, cancellationToken).ConfigureAwait(false))
                {
                    if (Rule(converter, stored) is { } rule)
                    {
                        breaches.Add(type.ClrType.Name, property.Name, rule, stored);
                    }
                }
            }
        }
    }

    // The converter between the id and its text, for a property the model holds as anything but text.
    private static ValueConverter? TextConverter(IProperty property)
    {
        var converter = property.GetTypeMapping().Converter;
        var model = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        return converter is { ProviderClrType: var provider } && provider == typeof(string) && model != typeof(string)
            ? converter
            : null;
    }

    private static string? Rule(ValueConverter converter, string stored)
    {
        object? id;
        try
        {
            id = converter.ConvertFromProvider(stored);
        }
#pragma warning disable CA1031 // whatever the converter throws for a text it cannot read is the finding itself
        catch (Exception)
#pragma warning restore CA1031
        {
            return Unreadable;
        }

        return id is not null && string.Equals(converter.ConvertToProvider(id) as string, stored, StringComparison.Ordinal)
            ? null
            : NotAsWritten;
    }
}
