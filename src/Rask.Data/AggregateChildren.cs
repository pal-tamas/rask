using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
/// What an aggregate holds: the collections of <see cref="Entity{TId}" /> children that travel with their root.
/// </summary>
/// <remarks>
/// <para>
/// A child is an <see cref="Entity{TId}" /> that is <b>not</b> an <see cref="Aggregate{TId}" />. That line is what
/// makes the rest of this file safe: a collection of aggregate roots is somebody else's data, so it is never loaded,
/// never synced and never deleted on the parent's behalf. RASK087 says so at compile time.
/// </para>
/// <para>
/// Read off the EF model rather than the CLR type, so whatever an app configured — a navigation through a private
/// backing field, a renamed table, a shadow foreign key — is what is walked. The answer is cached per model, which
/// is built once per context type.
/// </para>
/// </remarks>
internal static class AggregateChildren
{
    /// <summary>How deep a single aggregate may nest before Rask stops walking it.</summary>
    /// <remarks>
    /// An aggregate that is deeper than this is almost always several aggregates wearing one name. The limit also
    /// makes a cycle in the model impossible to loop on.
    /// </remarks>
    private const int MaxDepth = 4;

    private static readonly ConcurrentDictionary<(IModel Model, Type Entity), string[]> Cache = new();

    /// <summary>Whether <paramref name="clrType" /> is a child: an entity that is not a root of its own.</summary>
    public static bool IsChild(Type clrType) =>
        typeof(IEntity).IsAssignableFrom(clrType) && !typeof(IAggregate).IsAssignableFrom(clrType);

    /// <summary>
    /// The <c>Include</c> paths that load <paramref name="clrType" /> whole — <c>"Lines"</c>, <c>"Lines.Notes"</c>.
    /// </summary>
    public static string[] IncludePathsOf(DbContext context, Type clrType) =>
        Cache.GetOrAdd((context.Model, clrType), static key => Build(key.Model, key.Entity));

    /// <summary>Adds every child of <typeparamref name="TEntity" /> to <paramref name="query" />.</summary>
    public static IQueryable<TEntity> WithChildren<TEntity>(this IQueryable<TEntity> query, DbContext context)
        where TEntity : class
    {
        foreach (var path in IncludePathsOf(context, typeof(TEntity)))
        {
            query = query.Include(path);
        }

        return query;
    }

    /// <summary>
    /// Loads the children of an entity that is already tracked, for the paths a query could not carry.
    /// </summary>
    /// <remarks>
    /// One query per collection rather than a join, which is what keeps a root with two collections from
    /// multiplying its own rows. Only reached when the key could not be expressed as a predicate (a shadow or
    /// composite key), so the ordinary path is a single query with includes.
    /// </remarks>
    public static async Task LoadChildrenAsync(
        DbContext context, object entity, int depth = 0, CancellationToken cancellationToken = default)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        var entry = context.Entry(entity);

        foreach (var navigation in entry.Metadata.GetNavigations())
        {
            if (!navigation.IsCollection || !IsChild(navigation.TargetEntityType.ClrType))
            {
                continue;
            }

            var collection = entry.Collection(navigation.Name);

            if (!collection.IsLoaded)
            {
                await collection.LoadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (collection.CurrentValue is null)
            {
                continue;
            }

            foreach (var child in collection.CurrentValue)
            {
                await LoadChildrenAsync(context, child, depth + 1, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static string[] Build(IModel model, Type clrType)
    {
        var paths = new List<string>();
        Walk(model.FindEntityTypeOf(clrType), prefix: "", depth: 0, paths);
        return [.. paths];
    }

    private static void Walk(IEntityType? entityType, string prefix, int depth, List<string> paths)
    {
        if (entityType is null || depth >= MaxDepth)
        {
            return;
        }

        foreach (var navigation in entityType.GetNavigations())
        {
            if (!navigation.IsCollection || !IsChild(navigation.TargetEntityType.ClrType))
            {
                continue;
            }

            var path = prefix.Length == 0 ? navigation.Name : prefix + "." + navigation.Name;
            paths.Add(path);
            Walk(navigation.TargetEntityType, path, depth + 1, paths);
        }
    }
}
