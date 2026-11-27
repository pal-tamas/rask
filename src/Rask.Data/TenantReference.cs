using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     The foreign key from a table's <c>TenantId</c> to the tenant's own key, declared for every table that
///     <c>IBelongsTo&lt;Tenant&gt;</c> the aggregate that says <c>ITenant</c>: its name and the column it points at.
/// </summary>
/// <remarks>
///     <para>
///         Not a relationship of the model: EF Core holds a foreign key only between properties of one CLR type, and a
///         tenant is often a <see cref="Guid" /> to the tables and the text of a <c>string</c> key to its own row. So it
///         is an annotation on the dependent entity type — in the migration snapshot like every other — that
///         <c>RaskMigrationsModelDiffer</c> turns into the constraint, the same on every provider.
///     </para>
///     <para>
///         Written <c>Driver_tenantId_fkey -&gt; dbo.Tenant(id)</c>, so a snapshot reads as the DDL does.
///     </para>
/// </remarks>
internal sealed record TenantReference(string Name, string? PrincipalSchema, string PrincipalTable, string PrincipalColumn)
{
    /// <summary>The entity type annotation that carries a declared reference.</summary>
    internal const string Annotation = "Rask:TenantReference";

    private const string Arrow = " -> ";

    // Columns.TenantId, which a package without Rask.Data that links this file cannot name.
    private const string TenantIdProperty = "TenantId";

    /// <summary>The annotation's value.</summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Name}{Arrow}{(PrincipalSchema is null ? "" : PrincipalSchema + ".")}{PrincipalTable}({PrincipalColumn})");

    /// <summary>Reads the annotation's value back, or null when it is not one.</summary>
    internal static TenantReference? Parse(string? value)
    {
        var arrow = value?.LastIndexOf(Arrow, StringComparison.Ordinal) ?? -1;
        if (value is null || arrow <= 0 || !value.EndsWith(')'))
        {
            return null;
        }

        var principal = value[(arrow + Arrow.Length)..^1];
        var open = principal.LastIndexOf('(');
        if (open <= 0)
        {
            return null;
        }

        var table = principal[..open];
        var dot = table.IndexOf('.', StringComparison.Ordinal);
        return new TenantReference(
            value[..arrow],
            dot < 0 ? null : table[..dot],
            dot < 0 ? table : table[(dot + 1)..],
            principal[(open + 1)..]);
    }

    /// <summary>The references <paramref name="model" /> declares, by the table that holds each.</summary>
    internal static Dictionary<(string Table, string? Schema), (string Column, TenantReference Reference)> In(IRelationalModel? model)
    {
        var found = new Dictionary<(string, string?), (string, TenantReference)>();
        if (model is null)
        {
            return found;
        }

        foreach (var table in model.Tables)
        {
            foreach (var entityType in table.EntityTypeMappings.Select(static mapping => mapping.TypeBase).OfType<IEntityType>())
            {
                if (Parse(entityType.FindAnnotation(Annotation)?.Value as string) is { } reference
                    && entityType.FindProperty(TenantIdProperty) is { } property
                    && table.FindColumn(property) is { } column)
                {
                    found[(table.Name, table.Schema)] = (column.Name, reference);
                }
            }
        }

        return found;
    }
}
