using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     <c>rask audit</c>: every stored aggregate asked every rule its type declares today, and each one a rule refuses
///     reported by the rule and the field — read only, the ids alone, never what a row holds.
/// </summary>
/// <remarks>
///     <para>
///         A rule is asked of the rows a save writes, and of a stored row only the fields a save changes, so a rule tightened
///         since an old row was written says nothing about it until someone edits that field. This asks the stored rows
///         instead, before a new rule or a unique index is turned on: each aggregate is loaded whole, a page at a time, and
///         asked what a save adding it would ask — every validation attribute of every row of it, the type's own,
///         <c>IValidatableObject</c>, <c>Validate()</c>, awaited rules included — plus the rules of every value object it
///         holds (<see cref="StoredValueChecks" />), which a load never asks. Before any row is loaded, every id kept as
///         text is asked whether the app can read it (<see cref="StoredIdChecks" />).
///     </para>
///     <para>
///         Nothing is written: the audit's own context refuses a save, and the rules are asked as a dry run, so a
///         <c>Db.Lock</c> one takes is not taken. Every tenant's rows are read, each aggregate's rules asked in its own
///         tenant, so a rule that reads the read face sees what it would see in that tenant's save. A deleted row of a
///         soft-deleting aggregate is not asked: nothing opens it.
///     </para>
/// </remarks>
internal static class RuleAudit
{
    // Roots per read: the tracker is emptied after each, so an audit of a million rows holds a page of them.
    private const int Page = 200;

    // The same ceiling RowPaths and AggregateChildren walk with: deeper is several aggregates.
    private const int Deepest = 4;

    private static readonly MethodInfo RootsMethod =
        ((MethodCallExpression)((Expression<Func<Task>>)(() => AuditRootsAsync<object>(null!, null!, null!, default))).Body)
        .Method.GetGenericMethodDefinition();

    /// <summary>Asks every stored aggregate of the app's model — or only those named — every rule it declares.</summary>
    /// <param name="aggregates">The aggregates to ask, by type name (any case); empty for every one.</param>
    /// <param name="cancellationToken">Cancels the audit.</param>
    /// <returns>The rows read and every rule broken.</returns>
    /// <exception cref="ArgumentException">A name is no aggregate of the model; the message lists those that are.</exception>
    internal static async Task<RuleAuditReport> RunAsync(IReadOnlyCollection<string> aggregates, CancellationToken cancellationToken)
    {
        var context = Db.CreateOwnContext();
        await using (context.ConfigureAwait(false))
        {
            context.SavingChanges += static (_, _) =>
                throw new InvalidOperationException("rask audit only reads: nothing it asks may save.");
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            var roots = Roots(context.Model, aggregates);
            var findings = new RuleAuditFindings();
            using var everyTenant = Tenant.Across();
            using var dryRun = DryRun.Begin();

            await StoredIdChecks.Check(context, roots.SelectMany(RowTypes).Distinct(), findings, cancellationToken).ConfigureAwait(false);
            foreach (var root in roots)
            {
                await AuditRoots(context, root, findings, cancellationToken).ConfigureAwait(false);
            }

            return findings.Report();
        }
    }

    // Each aggregate root the model maps to a table, once for a hierarchy (its base's query reads the derived rows).
    private static List<IEntityType> Roots(IModel model, IReadOnlyCollection<string> named)
    {
        var all = model.GetEntityTypes()
            .Where(static t => typeof(IAggregate).IsAssignableFrom(t.ClrType) && t.BaseType is null && !t.IsOwned() &&
                               !t.HasSharedClrType && t.FindPrimaryKey() is not null && t.GetTableName() is not null)
            .OrderBy(static t => t.ClrType.Name, StringComparer.Ordinal)
            .ToList();
        if (named.Count == 0)
        {
            return all;
        }

        var unknown = named.Where(n => !all.Exists(t => Named(t, n))).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException(
                $"No aggregate is named {string.Join(", ", unknown.Select(static n => "'" + n + "'"))}. The model's aggregates are: "
                + string.Join(", ", all.Select(static t => t.ClrType.Name)) + ".",
                nameof(named));
        }

        return all.FindAll(t => named.Any(n => Named(t, n)));
    }

    private static bool Named(IEntityType type, string name) =>
        string.Equals(type.ClrType.Name, name, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type.ClrType.FullName, name, StringComparison.OrdinalIgnoreCase);

    // The aggregate's own types, its children's at every depth among them: whose ids the audit reads.
    private static IEnumerable<IEntityType> RowTypes(IEntityType root)
    {
        var seen = new HashSet<IEntityType>();
        var next = new Queue<(IEntityType Type, int Depth)>(root.GetDerivedTypesInclusive().Select(static t => (t, 0)));
        while (next.TryDequeue(out var at))
        {
            if (!seen.Add(at.Type) || at.Depth >= Deepest)
            {
                continue;
            }

            var children = at.Type.GetNavigations()
                .Select(static n => n.TargetEntityType)
                .Where(static t => !t.IsOwned() && AggregateChildren.IsChild(t.ClrType));
            foreach (var child in children)
            {
                next.Enqueue((child, at.Depth + 1));
            }
        }

        return seen;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type argument is an entity type of the context's model, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static Task AuditRoots(DbContext context, IEntityType root, RuleAuditFindings findings, CancellationToken cancellationToken) =>
        (Task)RootsMethod.MakeGenericMethod(root.ClrType).Invoke(null, [context, root, findings, cancellationToken])!;

    private static async Task AuditRootsAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, IEntityType type, RuleAuditFindings findings, CancellationToken cancellationToken)
        where TEntity : class
    {
        var name = type.ClrType.Name;
        var skip = 0;
        var full = true;
        while (full)
        {
            List<TEntity> page;
            try
            {
                page = await Ordered(context.Set<TEntity>().IgnoreQueryFilters([ModelBuilderExtensions.TenantFilter]), type)
                    .Skip(skip)
                    .Take(Page)
                    .WithChildren(context)
                    .AsSplitQuery()
                    .AsTracking()
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The id rules above say why, when an id is what could not be read; the rest of the type is not asked.
                findings.Add(name, "", "Rows could not be read, so the rest were not asked: " + e.GetBaseException().Message,
                    "from row " + (skip + 1).ToString(CultureInfo.InvariantCulture));
                return;
            }

            findings.Read(name, page.Count);
            foreach (var root in page)
            {
                await Audit(context, context.Entry(root), name, findings, cancellationToken).ConfigureAwait(false);
            }

            context.ChangeTracker.Clear();
            full = page.Count == Page;
            skip += Page;
        }
    }

    // By the key, so the pages neither overlap nor skip a row.
    private static IQueryable<TEntity> Ordered<TEntity>(IQueryable<TEntity> rows, IEntityType type)
        where TEntity : class
    {
        IOrderedQueryable<TEntity>? ordered = null;
        foreach (var name in type.FindPrimaryKey()!.Properties.Select(static p => p.Name))
        {
            ordered = ordered is null
                ? rows.OrderBy(e => EF.Property<object>(e, name))
                : ordered.ThenBy(e => EF.Property<object>(e, name));
        }

        return ordered ?? rows;
    }

    private static async Task Audit(
        DbContext context, EntityEntry root, string name, RuleAuditFindings findings, CancellationToken cancellationToken)
    {
        var id = IdOf(root);
        using var tenant = TenantOf(root);
        try
        {
            foreach (var failure in await DomainValidation.AuditAsync(context, root, cancellationToken).ConfigureAwait(false))
            {
                Add(failure);
            }

            foreach (var row in RowPaths.Of(root.Metadata, root.Entity).Rows)
            {
                foreach (var failure in StoredValueChecks.Of(context.Entry(row.Key), row.Value))
                {
                    Add(failure);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A rule that throws on an old row (a null nobody expected) is a finding about that row, not the audit's end.
            findings.Add(name, "", "A rule threw: " + e.GetType().Name + ": " + e.Message, id);
        }

        void Add(FieldFailure failure)
        {
            foreach (var field in failure.Fields.Count == 0 ? [""] : failure.Fields)
            {
                findings.Add(name, field, failure.Message, id);
            }
        }
    }

    private static string IdOf(EntityEntry root) =>
        string.Join(", ", root.Metadata.FindPrimaryKey()!.Properties.Select(p =>
            Convert.ToString(root.Property(p.Name).CurrentValue, CultureInfo.InvariantCulture)));

    // The aggregate's own tenant, so a rule reading the read face sees that tenant's rows; none for one with no tenant.
    private static IDisposable? TenantOf(EntityEntry root) =>
        root.Metadata.FindProperty(Columns.TenantId) is null
            ? null
            : root.Property(Columns.TenantId).CurrentValue switch
            {
                Guid tenant when tenant != Guid.Empty => Tenant.Use(tenant),
                int tenant when tenant > 0 => Tenant.Use(tenant),
                long tenant when tenant > 0 => Tenant.Use(tenant),
                _ => null,
            };
}
