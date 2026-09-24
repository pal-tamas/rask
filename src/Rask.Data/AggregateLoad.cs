using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     Loading one aggregate, whole and by its key — the shape every by-id fetch in Rask.Data needs.
/// </summary>
/// <remarks>
///     <para>
///         Shared because two callers need exactly this and must not disagree about it:
///         <see cref="TestDatabase.LoadAsync{TEntity}(object, CancellationToken)" />, which is what a test
///         asserts a write with, and the generated <c>Product.ModelAsync(id)</c>, which is what an edit form
///         is filled from. A test that loaded a row the application could not would be a test of nothing.
///     </para>
///     <para>
///         It is a <c>Where</c> and not EF Core's <c>Find</c> on purpose. <c>Find</c> bypasses query filters,
///         so a soft-deleted row comes back from it and not from a query — and an edit form opened on a
///         deleted row is the exact bug that would hide behind that difference.
///     </para>
/// </remarks>
internal static class AggregateLoad
{
    private static readonly FieldInfo BoxedValue =
        typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    /// <summary>Loads one aggregate and its children, untracked, through <paramref name="context" />.</summary>
    /// <param name="context">The context to read through. Not disposed here.</param>
    /// <param name="keyValues">The key's values, in the order the key declares them.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    internal static async Task<TEntity?> FindAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, object?[] keyValues, CancellationToken cancellationToken)
        where TEntity : class, IAggregate
    {
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
            // Loading ONE root by its key loads the aggregate whole — its children come with it. A query
            // (Product.Read.Where(…)) deliberately does not: listing a thousand roots should not drag in
            // every line each of them holds. See docs/data.md.
            return await context.Set<TEntity>()
                .AsNoTracking()
                .WithChildren(context)
                .FirstOrDefaultAsync(predicate, cancellationToken)
                .ConfigureAwait(false);
        }

        // A key part with no CLR property (a shadow key) or a type with no equality operator cannot be
        // expressed as a predicate here; EF Core's own Find can. It tracks what it returns and skips the
        // query filters, which is why it is the fallback and not the path.
        var found = await context.Set<TEntity>().FindAsync(keyValues, cancellationToken).ConfigureAwait(false);

        if (found is not null)
        {
            await AggregateChildren
                .LoadChildrenAsync(context, found, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return found;
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
