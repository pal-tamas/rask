using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Rask.Data;

/// <summary>
///     Resolves each <c>b.NoOverlap(…)</c> to the columns of its table and the provider's SQL for the rows it holds, and
///     forgets the declaration — so the model a migration is made from carries the rule as a string alone.
/// </summary>
/// <remarks>
///     It also names a trigger on the entity: SQL Server refuses <c>OUTPUT</c> without <c>INTO</c> on a table with
///     triggers, and EF Core writes its saves around them once it knows. A trigger named on PostgreSQL's table, whose rule
///     is a constraint, changes nothing there.
/// </remarks>
/// <param name="sql">The provider's dialect, or null for a provider that writes no SQL.</param>
internal sealed class NoOverlapConvention(ModelSql? sql) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var limit = modelBuilder.Metadata.GetMaxIdentifierLength();

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.FindAnnotation(NoOverlapDeclaration.Annotation)?.Value is not List<NoOverlapDeclaration> declared)
            {
                continue;
            }

            entityType.RemoveAnnotation(NoOverlapDeclaration.Annotation);
            var table = entityType.GetTableName()
                ?? throw new InvalidOperationException(
                    $"'{entityType.DisplayName()}' declares NoOverlap but is mapped to no table, so no database can hold the rule.");

            var rules = new List<NoOverlapRule>();
            foreach (var declaration in declared)
            {
                var rule = Resolve(entityType, table, declaration, limit, rules);
                rules.Add(rule);
                if (declaration.Rows is { } rows)
                {
                    NoOverlapRows.Remember(entityType.ClrType, rule.Name, rows.Lambda);
                }

                entityType.Builder.HasTrigger(NoOverlapSql.Trigger(rule.Name, limit));
            }

            entityType.SetAnnotation(NoOverlapRule.Annotation, NoOverlapRule.Write(rules));
        }
    }

    private NoOverlapRule Resolve(
        IConventionEntityType entityType, string table, NoOverlapDeclaration declaration, int limit, List<NoOverlapRule> earlier)
    {
        var keys = declaration.Keys.Select(name => Scalar(entityType, name, "key")).ToArray();
        var (from, fromPath, to, toPath) = declaration.Range is { } range
            ? Ends(entityType, range)
            : (Scalar(entityType, declaration.From!, "start"), declaration.From!, Scalar(entityType, declaration.To!, "end"), declaration.To!);

        var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());
        string Column(IConventionProperty property) => property.GetColumnName(store) ?? property.GetColumnName();

        var stem = $"EX_{table}_{string.Join('_', keys.Select(Column))}_{Column(from)}";
        var name = Limited(stem, limit);
        var n = 1;
        while (earlier.Exists(r => string.Equals(r.Name, name, StringComparison.Ordinal)))
        {
            n++;
            name = Limited(stem, limit - 2) + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new NoOverlapRule(
            name,
            [.. keys.Select(Column)],
            declaration.Keys,
            Column(from),
            fromPath,
            Column(to),
            toPath,
            Rows(entityType, store, declaration),
            declaration.Message,
            declaration.Range ?? declaration.From!);
    }

    // The rule's own Where(…) and the soft delete, in the provider's SQL.
    private string Rows(IConventionEntityType entityType, StoreObjectIdentifier store, NoOverlapDeclaration declaration)
    {
        if (sql is null)
        {
            return "";
        }

        var terms = new List<string>();
        if (declaration.Rows is { } rows)
        {
            sql.Supported(rows.Declaration);
            terms.Add(ModelExpressionSql.Filter(sql, entityType, rows.Lambda, rows.Declaration));
        }

        if (ConventionRegistry.Has(entityType.ClrType, TraitSet.SoftDelete) && entityType.FindProperty(Columns.DeletedAt) is { } deletedAt)
        {
            terms.Add($"{sql.Column(deletedAt.GetColumnName(store) ?? deletedAt.GetColumnName())} IS NULL");
        }

        return string.Join(" AND ", terms);
    }

    private static IConventionProperty Scalar(IConventionEntityType entityType, string name, string role) =>
        entityType.FindProperty(name)
        ?? throw new InvalidOperationException(
            entityType.FindComplexProperty(name) is not null
                ? $"'{entityType.DisplayName()}.{name}' is a value object of several columns, and a NoOverlap {role} is one column: name a property of one."
                : $"'{entityType.DisplayName()}' declares NoOverlap over '{name}', which is not a mapped property. Check the name, and that it is not ignored.");

    // A Range value object's two ends, as its record declares them: the first member of its primary constructor is the
    // start, the second the end.
    private static (IConventionProperty From, string FromPath, IConventionProperty To, string ToPath) Ends(
        IConventionEntityType entityType, string range)
    {
        if (entityType.FindComplexProperty(range) is not { IsCollection: false } complex || complex.ComplexType.IsMappedToJson())
        {
            throw new InvalidOperationException(
                $"'{entityType.DisplayName()}.{range}' is no Range value object of its own columns. Name one "
                + "(x => x.Period), or give the start and the end: b.NoOverlap(key, x => x.From, x => x.To, message).");
        }

        var (start, end) = RangeMembers(complex.ClrType)
            ?? throw new InvalidOperationException(
                $"'{entityType.DisplayName()}.{range}' is a {complex.ClrType.Name}, which declares no start and end the way a "
                + "Range does: record Period(DateTime From, DateTime? To) : Range(From, To).");

        return (Member(complex, start), $"{range}.{start}", Member(complex, end), $"{range}.{end}");
    }

    private static IConventionProperty Member(IConventionComplexProperty complex, string name) =>
        complex.ComplexType.FindProperty(name)
        ?? throw new InvalidOperationException($"'{complex.ComplexType.DisplayName()}.{name}' is not mapped to a column.");

    // The record's positional parameters: its public constructor of two that its properties are named after.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "A complex property's CLR type, whose constructors and properties EF Core already keeps to build it.")]
    private static (string Start, string End)? RangeMembers(Type type)
    {
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 2 &&
                parameters.All(p => type.GetProperty(p.Name ?? "", BindingFlags.Public | BindingFlags.Instance) is not null))
            {
                return (parameters[0].Name!, parameters[1].Name!);
            }
        }

        return null;
    }

    private static string Limited(string name, int limit) => name.Length > limit ? name[..limit] : name;
}
