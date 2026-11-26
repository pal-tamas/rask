using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Before a delete that is only tried (<see cref="DryRun" />): whether a row still points at what it would remove, through a
///     relationship the database would refuse it by — the sentence a refused delete says (<c>WhenReferenced("…")</c>,
///     <c>OnDelete(behavior, "…")</c>), told before the delete is tried.
/// </summary>
/// <remarks>
///     <para>
///         One <c>EXISTS</c> per relationship that has a sentence and does not cascade, over its foreign key's columns, with
///         every filter lifted — the database's key counts another tenant's row and a soft-deleted one as well. The first
///         that holds a row answers, as the database answers with the first constraint it trips.
///     </para>
///     <para>
///         A row that is only marked deleted (<c>SoftDelete</c>, <c>SoftRemoved</c>) asks nothing: its delete is an update.
///         Nor does a relationship whose own rows the delete removes too — which of them stay is the save's to say.
///     </para>
/// </remarks>
internal static class ReferencedDeleteCheck
{
    private static readonly MethodInfo AnyMethod =
        ((MethodCallExpression)((Expression<Func<Task<bool>>>)(() => AnyAsync<object>(null!, null!, default))).Body)
        .Method.GetGenericMethodDefinition();

    private static readonly FieldInfo BoxedValue = typeof(StrongBox<object?>).GetField(nameof(StrongBox<object?>.Value))!;

    /// <summary>What the delete <paramref name="context" /> tracks would be refused for by a row still pointing at it.</summary>
    internal static async Task<List<FieldFailure>> FailuresAsync(DbContext context, CancellationToken cancellationToken)
    {
        var deleted = context.ChangeTracker.Entries()
            .Where(static e => e.State == EntityState.Deleted && e.Metadata.FindProperty(Columns.DeletedAt) is null)
            .ToList();
        if (deleted.Count == 0)
        {
            return [];
        }

        var types = deleted.Select(static e => e.Metadata).Distinct().ToList();

        foreach (var key in ReferenceProbe.Refusing(types))
        {
            if (types.Exists(type => key.DeclaringEntityType.IsAssignableFrom(type) || type.IsAssignableFrom(key.DeclaringEntityType)) ||
                (ReferencedDelete.SentenceOf(key) ?? ReferencedDelete.GeneralSentenceOf(key.PrincipalEntityType)) is not { } sentence)
            {
                continue;
            }

            var rows = deleted.Where(e => key.PrincipalEntityType.IsAssignableFrom(e.Metadata)).ToList();
            if (rows.Count > 0 && await HeldAsync(context, key, rows, cancellationToken).ConfigureAwait(false))
            {
                return [ReferencedDelete.About(sentence, key.GetConstraintName())];
            }
        }

        return [];
    }

    // Whether a row of the key's table points at any of the deleted rows: one EXISTS, the rows OR'd.
    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type argument is an entity type of the context's model, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static Task<bool> HeldAsync(DbContext context, IForeignKey key, List<EntityEntry> rows, CancellationToken cancellationToken)
    {
        var dependent = Expression.Parameter(key.DeclaringEntityType.ClrType, "row");
        Expression? any = null;

        foreach (var row in rows)
        {
            Expression? same = null;
            for (var i = 0; i < key.Properties.Count; i++)
            {
                var property = key.Properties[i];
                var value = row.OriginalValues[key.PrincipalKey.Properties[i]];
                var stored = StoredProperty.Of(dependent, property);
                var equal = Expression.Equal(
                    stored,
                    Expression.Convert(Expression.Field(Expression.Constant(new StrongBox<object?>(value)), BoxedValue), stored.Type));
                same = same is null ? equal : Expression.AndAlso(same, equal);
            }

            any = any is null ? same : Expression.OrElse(any, same!);
        }

        return (Task<bool>)AnyMethod.MakeGenericMethod(key.DeclaringEntityType.ClrType)
            .Invoke(null, [context, Expression.Lambda(any!, dependent), cancellationToken])!;
    }

    private static Task<bool> AnyAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, LambdaExpression held, CancellationToken cancellationToken)
        where TEntity : class =>
        context.Set<TEntity>().AsNoTracking().IgnoreQueryFilters().AnyAsync((Expression<Func<TEntity, bool>>)held, cancellationToken);
}
