using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Before a save: every reference a <see cref="PerTenant" /> row sets to another tenant-scoped table names a row of
///     the tenant in flight — never one of another tenant's, and never one that is not there — or the save is refused
///     with a <see cref="RaskValidationException" /> under the reference's field.
/// </summary>
/// <remarks>
///     <para>
///         The database's foreign key only says the row exists, in any tenant, so without this a form posting another
///         tenant's id — or a method handed one — links across tenants. A reference is asked about only when the save
///         sets it: on an added row, or a changed one whose key changed. A row the context is adding in the same save is
///         the tenant's by construction and asks nothing.
///     </para>
///     <para>
///         <b>One query per referenced table per save</b>, through that table's own filters — the tenant's (a child's
///         and a <see cref="PerTenantThrough{TParent}" /> aggregate's through its parent) and the soft delete's — for
///         every key the save sets to it at once: <c>WHERE Id IN (…)</c>. A key of several columns is asked row by row.
///     </para>
///     <para>
///         The sentence is the relationship's <c>WhenMissing("…")</c>, else the referenced entity's, else the strings
///         catalog's <c>ValidationNotFound</c> — the same for a row that is not there and one of another tenant's, so
///         the answer never says another tenant has it. <see cref="Tenant.Across" /> is the deliberate way round.
///     </para>
///     <para>
///         A row of NO tenant — <c>TenantId</c> NULL: an administrator's account, which belongs to no customer — is
///         nobody's to keep apart, so any tenant may name it: a note an administrator writes inside
///         <c>Tenant.Use(partner)</c> names its author and saves. It is asked only for a key the tenant's own read
///         missed, so a save that names the tenant's rows asks nothing more.
///     </para>
/// </remarks>
internal static class ReferencedTenantCheck
{
    /// <summary>The relationship annotation <c>WhenMissing("…")</c> writes.</summary>
    internal const string RelationshipAnnotation = "Rask:WhenMissingMessage";

    /// <summary>The entity type annotation <c>WhenMissing("…")</c> writes.</summary>
    internal const string EntityAnnotation = "Rask:WhenMissingEntityMessage";

    private static readonly MethodInfo FoundMethod =
        ((MethodCallExpression)((Expression<Func<Task<HashSet<object>>>>)(() => FoundAsync<object, object>(null!, null!, null!, false, default))).Body)
        .Method.GetGenericMethodDefinition();

    // The references each entity type's save checks, read once off the model.
    private static readonly ConditionalWeakTable<IEntityType, IForeignKey[]> Checked = [];

    // Whether a model has any reference to check: a save of one that has none asks nothing.
    private static readonly ConditionalWeakTable<IModel, StrongBox<bool>> Models = [];

    /// <summary>Refuses the save when a reference it sets names no row the tenant in flight reads.</summary>
    /// <exception cref="RaskValidationException">One does: a failure under each such reference's field.</exception>
    internal static async Task RefuseAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (Tenant.IsAcrossTenants || Tenant.IsUnresolved ||
            !Models.GetValue(context.Model, static m => new(m.GetEntityTypes().Any(t => KeysOf(t).Length > 0))).Value)
        {
            return;
        }

        var asked = Asked(context);
        if (asked.Count == 0)
        {
            return;
        }

        var failures = new List<FieldFailure>();
        var said = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (principalKey, rows) in asked)
        {
            var missing = await MissingAsync(context, principalKey, [.. rows.Select(static r => r.Values)], cancellationToken)
                .ConfigureAwait(false);

            foreach (var (entry, key, _) in rows.Where(r => missing.Contains(Joined(r.Values))))
            {
                var fields = FieldsOf(context, entry, key);
                if (said.Add(string.Join('\u001f', fields)))
                {
                    failures.Add(new FieldFailure(SentenceOf(key), fields, Source: key.GetConstraintName()));
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new RaskValidationException(failures);
        }
    }

    // Every reference the save sets, by the key of the table it points at; a row added beside it is left out.
    private static Dictionary<IKey, List<(EntityEntry Entry, IForeignKey Key, object?[] Values)>> Asked(DbContext context)
    {
        var asked = new Dictionary<IKey, List<(EntityEntry, IForeignKey, object?[])>>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var key in KeysOf(entry.Metadata))
            {
                if (SetBy(entry, key) is not { } values ||
                    TrackedPrincipals.Find(context, key, values) is { State: EntityState.Added })
                {
                    continue;
                }

                if (!asked.TryGetValue(key.PrincipalKey, out var rows))
                {
                    asked[key.PrincipalKey] = rows = [];
                }

                rows.Add((entry, key, values));
            }
        }

        return asked;
    }

    // The references to another tenant-scoped table a PerTenant row holds — not the one that holds a child in its
    // aggregate, whose tenant is its parent's (ParentTenantCheck).
    private static IForeignKey[] KeysOf(IEntityType entityType) =>
        Checked.GetValue(entityType, static type =>
            !ConventionRegistry.IsPerTenant(type.ClrType)
                ? []
                : [.. type.GetForeignKeys().Where(key =>
                    !key.PrincipalEntityType.IsOwned() &&
                    TenantParents.IsPartitioned(key.PrincipalEntityType.ClrType) &&
                    !(key.PrincipalToDependent is not null && !typeof(IAggregate).IsAssignableFrom(type.ClrType)))]);

    // The key's values the save writes, when it sets them: every one of an added row's, a changed row's only when one
    // of them changed. Null for a reference left as it was, or one with a NULL part.
    private static object?[]? SetBy(EntityEntry entry, IForeignKey key)
    {
        var values = new object?[key.Properties.Count];
        var changed = entry.State == EntityState.Added;

        for (var i = 0; i < values.Length; i++)
        {
            var property = entry.Property(key.Properties[i].Name);
            values[i] = property.CurrentValue;

            if (values[i] is null)
            {
                return null;
            }

            changed |= property.IsModified && !Equals(property.OriginalValue, values[i]);
        }

        return changed ? values : null;
    }

    // The value tuples of `values` no row of the principal table answers to, as Joined spells them.
    private static async Task<HashSet<string>> MissingAsync(
        DbContext context, IKey principalKey, List<object?[]> values, CancellationToken cancellationToken)
    {
        var distinct = values.DistinctBy(Joined, StringComparer.Ordinal).ToList();
        var missing = new HashSet<string>(StringComparer.Ordinal);

        if (principalKey.Properties is [var only])
        {
            var principal = principalKey.DeclaringEntityType;
            var found = await Found(context, principal, only, [.. distinct.Select(static v => v[0]!)], ownerless: false, cancellationToken)
                .ConfigureAwait(false);
            var unseen = distinct.Where(v => !found.Contains(v[0]!)).ToList();

            // A row of no tenant — an administrator's account — is nobody's to keep apart, so any tenant may name it.
            if (unseen.Count > 0 && principal.FindProperty(Columns.TenantId) is not null && ConventionRegistry.IsPerTenant(principal.ClrType))
            {
                found = await Found(context, principal, only, [.. unseen.Select(static v => v[0]!)], ownerless: true, cancellationToken)
                    .ConfigureAwait(false);
                unseen.RemoveAll(v => found.Contains(v[0]!));
            }

            missing.UnionWith(unseen.Select(Joined));
            return missing;
        }

        // A key of several columns: rare enough to ask row by row, through the parent check's own question.
        var reference = principalKey.GetReferencingForeignKeys().First();
        foreach (var tuple in distinct)
        {
            if (!await ParentTenantCheck.VisibleAsync(context, reference, tuple, cancellationToken).ConfigureAwait(false))
            {
                missing.Add(Joined(tuple));
            }
        }

        return missing;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type arguments are a mapped entity type and its key's type, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type, and its key's own type.")]
    private static Task<HashSet<object>> Found(
        DbContext context, IReadOnlyEntityType principal, IProperty key, List<object> ids, bool ownerless, CancellationToken cancellationToken) =>
        (Task<HashSet<object>>)FoundMethod.MakeGenericMethod(principal.ClrType, key.ClrType)
            .Invoke(null, [context, key, ids, ownerless, cancellationToken])!;

    // SELECT Key FROM Principal WHERE Key IN (…), through the principal's filters: the keys the tenant in flight reads.
    // Ownerless, the tenant's filter is lifted for the rows whose TenantId is NULL; the soft delete's still stands.
    private static async Task<HashSet<object>> FoundAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, TKey>(
        DbContext context, IProperty key, List<object> ids, bool ownerless, CancellationToken cancellationToken)
        where TEntity : class
    {
        var typed = new StrongBox<List<TKey>>([.. ids.Cast<TKey>()]);
        var row = Expression.Parameter(typeof(TEntity), "row");
        var stored = StoredProperty.Of(row, key);
        Expression held = Expression.Call(Expression.Field(Expression.Constant(typed), Keys<TKey>.Value), Keys<TKey>.Contains, stored);
        var rows = context.Set<TEntity>().AsNoTracking();

        if (ownerless)
        {
            var tenant = key.DeclaringType.ContainingEntityType.FindProperty(Columns.TenantId)!;
            held = Expression.AndAlso(held, Expression.Equal(StoredProperty.Of(row, tenant), Expression.Constant(null, tenant.ClrType)));
            rows = rows.IgnoreQueryFilters([ModelBuilderExtensions.TenantFilter]);
        }

        var found = await rows
            .Where(Expression.Lambda<Func<TEntity, bool>>(held, row))
            .Select(Expression.Lambda<Func<TEntity, TKey>>(stored, row))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. found.Select(static k => (object)k!)];
    }

    // The list a query asks the keys of, and how it asks: a field of a box, so the list is a parameter of the SQL.
    private static class Keys<TKey>
    {
        internal static readonly FieldInfo Value = typeof(StrongBox<List<TKey>>).GetField(nameof(StrongBox<List<TKey>>.Value))!;

        internal static readonly MethodInfo Contains =
            ((MethodCallExpression)((Expression<Func<List<TKey>, TKey, bool>>)((list, key) => list.Contains(key))).Body).Method;
    }

    // The form's fields for the reference: its properties, under the row's place on its aggregate's form.
    private static List<string> FieldsOf(DbContext context, EntityEntry entry, IForeignKey key)
    {
        var row = RowPath.Of(context, entry);
        return [.. key.Properties.Select(p => row + p.Name)];
    }

    private static string SentenceOf(IForeignKey key) =>
        key.FindAnnotation(RelationshipAnnotation)?.Value as string
        ?? key.PrincipalEntityType.FindAnnotation(EntityAnnotation)?.Value as string
        ?? HeldRowReference.Sentence();

    private static string Joined(object?[] values) => string.Join('\u001f', values);
}
