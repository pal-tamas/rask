using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Puts the read half of <see cref="DbSet{TEntity}" /> on the model type itself, so
///     <c>Product.Where(…)</c>, <c>Product.FindAsync(…)</c> and <c>Product.AsQueryable()</c> need no
///     <see cref="DbContext" /> in scope to reach them through.
/// </summary>
/// <remarks>
///     <para>
///         These are C# 14 static extension members over every type deriving from <see cref="Model" />,
///         which is why nothing has to be declared or derived from a second base: an entity that compiles
///         today has them.
///     </para>
///     <para>
///         <b>Every read is untracked, and every read opens and disposes its own context.</b> A Rask page
///         lives as long as the browser keeps its socket open, and a <see cref="DbContext" /> is neither
///         thread-safe nor meant to accumulate a session's worth of entities — so nothing here holds one
///         between calls, and nothing a read returns is being watched for changes.
///     </para>
///     <para>
///         <b>Writes are not here.</b> The source generator gives every model a form-shaped companion and
///         the writes that take it — <c>Product.CreateAsync(ProductModel)</c>,
///         <c>Product.UpdateAsync(id, ProductModel)</c>, <c>Product.DeleteAsync(id)</c>. Anything richer — a
///         domain operation such as <c>order.Cancel()</c>, several changes in one transaction — is ordinary
///         EF Core: inject the context and call <c>SaveChangesAsync</c>.
///     </para>
///     <para>
///         A member declared on the entity itself always wins over one of these, so an entity with its own
///         static <c>Find</c> keeps it.
///     </para>
/// </remarks>
public static class ModelSet
{
    private static readonly FieldInfo BoxedValue = typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    extension<TEntity>(TEntity)
        where TEntity : Model
    {
        // ---- The set itself -------------------------------------------------------------------

        /// <summary>
        ///     The whole set, as a query — the starting point for anything the operators below do not open
        ///     directly.
        /// </summary>
        /// <example>
        ///     <code>
        /// var deleted = await Product.All
        ///     .IgnoreQueryFilters()
        ///     .Where(p =&gt; p.DeletedAt != null)
        ///     .ToListAsync();
        ///     </code>
        /// </example>
        public static ModelQuery<TEntity> All => new();

        /// <summary>
        ///     The whole set as a standard <see cref="IQueryable{T}" /> that holds no context — the shape a
        ///     component that composes its own LINQ, such as a data grid, takes.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>UiDataGrid.Data(Product.AsQueryable())</c> sorts with <c>ORDER BY</c> and pages with
        ///         <c>Skip</c>/<c>Take</c> in the database. Each time the queryable is executed — a
        ///         <c>Count()</c>, a <c>ToList()</c>, an <c>await ToListAsync()</c> — a context is opened
        ///         for that execution and disposed after it, so the queryable is safe to keep in a field of
        ///         a page for as long as the page lives.
        ///     </para>
        ///     <para>
        ///         EF Core's own operators (<c>Include</c>, <c>IgnoreQueryFilters</c>, <c>AsSplitQuery</c>)
        ///         only apply to EF's own query provider, so put them on the model query first:
        ///         <c>Product.Include(p =&gt; p.Reviews).AsQueryable()</c>.
        ///     </para>
        /// </remarks>
        public static IQueryable<TEntity> AsQueryable() => new ModelQuery<TEntity>().AsQueryable();

        // ---- Query entry points: each runs against its own context ------------------------------

        /// <summary>Filters the set.</summary>
        public static ModelQuery<TEntity> Where(Expression<Func<TEntity, bool>> predicate) =>
            new ModelQuery<TEntity>().Where(predicate);

        /// <summary>Orders the set ascending.</summary>
        public static ModelQuery<TEntity> OrderBy<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
            new ModelQuery<TEntity>().OrderBy(keySelector);

        /// <summary>Orders the set descending.</summary>
        public static ModelQuery<TEntity> OrderByDescending<TKey>(Expression<Func<TEntity, TKey>> keySelector) =>
            new ModelQuery<TEntity>().OrderByDescending(keySelector);

        /// <summary>Eager-loads a related navigation.</summary>
        public static ModelQuery<TEntity> Include<TProperty>(Expression<Func<TEntity, TProperty>> navigation) =>
            new ModelQuery<TEntity>().Include(navigation);

        /// <summary>Eager-loads a related navigation named by a path.</summary>
        public static ModelQuery<TEntity> Include(string navigationPropertyPath) =>
            new ModelQuery<TEntity>().Include(navigationPropertyPath);

        /// <summary>Projects the set, reading only the columns the projection names.</summary>
        public static Projection<TEntity, TResult> Select<TResult>(Expression<Func<TEntity, TResult>> selector) =>
            new ModelQuery<TEntity>().Select(selector);

        /// <summary>Takes the first <paramref name="count" /> rows of the unordered set.</summary>
        public static ModelQuery<TEntity> Take(int count) => new ModelQuery<TEntity>().Take(count);

        /// <summary>Skips <paramref name="count" /> rows of the unordered set.</summary>
        public static ModelQuery<TEntity> Skip(int count) => new ModelQuery<TEntity>().Skip(count);

        /// <summary>
        ///     Drops the model's global query filters, including the one that hides soft-deleted rows.
        /// </summary>
        public static ModelQuery<TEntity> IgnoreQueryFilters() => new ModelQuery<TEntity>().IgnoreQueryFilters();

        /// <summary>Splits the query's joins into separate round trips.</summary>
        public static ModelQuery<TEntity> AsSplitQuery() => new ModelQuery<TEntity>().AsSplitQuery();

        // ---- Terminal reads over the whole set --------------------------------------------------

        /// <summary>Finds the row with this primary key, or <c>null</c>.</summary>
        /// <remarks>
        ///     <para>
        ///         An untracked query by key, like every other read here — the row that comes back is a
        ///         plain object nothing is watching. Global query filters apply, so a soft-deleted row is
        ///         not found.
        ///     </para>
        ///     <para>
        ///         The key is typed <see cref="object" /> for the same reason EF Core types it that way: an
        ///         entity's key may be composite, and the key type is not inferable from the entity type at
        ///         a static call site.
        ///     </para>
        /// </remarks>
        /// <exception cref="ArgumentException">The key is null or not of the key property's type.</exception>
        public static Task<TEntity?> FindAsync(object key, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            return FindByKeyAsync<TEntity>([key], cancellationToken);
        }

        /// <summary>Finds the row with this composite key, or <c>null</c>.</summary>
        /// <exception cref="ArgumentException">
        ///     The number of values does not match the key, or one is null or of the wrong type.
        /// </exception>
        public static Task<TEntity?> FindAsync(object?[] keyValues, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(keyValues);
            return FindByKeyAsync<TEntity>(keyValues, cancellationToken);
        }

        /// <summary>Reads the whole set.</summary>
        public static Task<List<TEntity>> ToListAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().ToListAsync(cancellationToken);

        /// <summary>Reads the whole set as an array.</summary>
        public static Task<TEntity[]> ToArrayAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().ToArrayAsync(cancellationToken);

        /// <summary>The first row matching <paramref name="predicate" />, or <c>null</c>.</summary>
        public static Task<TEntity?> FirstOrDefaultAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).FirstOrDefaultAsync(cancellationToken);

        /// <summary>The only row matching <paramref name="predicate" />, or <c>null</c>.</summary>
        /// <exception cref="InvalidOperationException">More than one row matched.</exception>
        public static Task<TEntity?> SingleOrDefaultAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).SingleOrDefaultAsync(cancellationToken);

        /// <summary>How many rows the set has.</summary>
        public static Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().CountAsync(cancellationToken);

        /// <summary>How many rows match <paramref name="predicate" />.</summary>
        public static Task<int> CountAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).CountAsync(cancellationToken);

        /// <summary>How many rows the set has, as a <see cref="long" />.</summary>
        public static Task<long> LongCountAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().LongCountAsync(cancellationToken);

        /// <summary>Whether the set has any row at all.</summary>
        public static Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().AnyAsync(cancellationToken);

        /// <summary>Whether any row matches <paramref name="predicate" />.</summary>
        public static Task<bool> AnyAsync(
            Expression<Func<TEntity, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().Where(predicate).AnyAsync(cancellationToken);

        /// <summary>Streams the whole set, a row at a time.</summary>
        public static IAsyncEnumerable<TEntity> AsAsyncEnumerable(CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().AsAsyncEnumerable(cancellationToken);

        /// <summary>
        ///     Runs <paramref name="query" /> against the set's live <see cref="IQueryable{T}" />, for the
        ///     shapes this surface does not wrap — a group-by, a join, an aggregate.
        /// </summary>
        /// <remarks>
        ///     The context is opened for the call and disposed after it. Do not let the
        ///     <see cref="IQueryable{T}" /> escape the callback — reach for <c>AsQueryable()</c> when the
        ///     query has to outlive one call.
        /// </remarks>
        public static Task<TResult> QueryAsync<TResult>(
            Func<IQueryable<TEntity>, CancellationToken, Task<TResult>> query,
            CancellationToken cancellationToken = default) =>
            new ModelQuery<TEntity>().QueryAsync(query, cancellationToken);
    }

    private static async Task<TEntity?> FindByKeyAsync<TEntity>(object?[] keyValues, CancellationToken cancellationToken)
        where TEntity : Model
    {
        await using var context = Db.CreateContext();

        var primaryKey = context.Model.FindEntityType(typeof(TEntity))?.FindPrimaryKey()
                         ?? throw new InvalidOperationException(
                             $"'{typeof(TEntity).Name}' is not mapped with a primary key by the configured " +
                             "context, so there is nothing to find it by.");

        if (primaryKey.Properties.Count != keyValues.Length)
        {
            throw new ArgumentException(
                $"'{typeof(TEntity).Name}' has a key of {primaryKey.Properties.Count} value(s), but " +
                $"{keyValues.Length} were given.",
                nameof(keyValues));
        }

        if (KeyPredicate<TEntity>(primaryKey, keyValues) is { } predicate)
        {
            return await context.Set<TEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(predicate, cancellationToken)
                .ConfigureAwait(false);
        }

        // A key part with no CLR property (a shadow key) or a type with no equality operator cannot be
        // expressed as a predicate here; EF Core's own Find can. The context is disposed on return, so the
        // row it tracks is released with it.
        return await context.Set<TEntity>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false);
    }

    // row => row.K1 == @k1 && row.K2 == @k2. The values are read through a StrongBox rather than inlined
    // as constants, so EF Core sees parameters: one cached query plan for every key, not one per value.
    private static Expression<Func<TEntity, bool>>? KeyPredicate<TEntity>(IKey primaryKey, object?[] keyValues)
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        Expression? body = null;

        for (var i = 0; i < keyValues.Length; i++)
        {
            var property = primaryKey.Properties[i];
            var value = keyValues[i]
                        ?? throw new ArgumentException(
                            $"The key value at position {i} is null, and '{typeof(TEntity).Name}.{property.Name}' " +
                            "is part of the primary key.",
                            nameof(keyValues));

            if (!property.ClrType.IsInstanceOfType(value))
            {
                throw new ArgumentException(
                    $"The key value at position {i} is a {value.GetType().Name}, but " +
                    $"'{typeof(TEntity).Name}.{property.Name}' is a {property.ClrType.Name}.",
                    nameof(keyValues));
            }

            if (property.PropertyInfo is not { } clrProperty)
            {
                return null;
            }

            var parameter = Expression.Convert(
                Expression.Field(Expression.Constant(new StrongBox<object?>(value)), BoxedValue),
                property.ClrType);

            BinaryExpression equal;
            try
            {
                equal = Expression.Equal(Expression.Property(row, clrProperty), parameter);
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            body = body is null ? equal : Expression.AndAlso(body, equal);
        }

        return body is null ? null : Expression.Lambda<Func<TEntity, bool>>(body, row);
    }
}
