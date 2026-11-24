using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>
///     <c>b.Json(t =&gt; t.LocalWells, column: "LocalSettings")</c> — a value object, or a list of them, kept whole in one
///     JSON column rather than spread over one column per member.
/// </summary>
public static class JsonBuilderExtensions
{
    /// <summary>
    ///     Stores <paramref name="member" /> — a value object of several members, or a collection of them — as one JSON
    ///     column, everything it holds inside: its own lists (<see cref="Rask.Values.ValueList{T}" />), the value objects
    ///     it holds, a <see cref="Rask.Values.Secret{T}" /> as its ciphertext.
    /// </summary>
    /// <typeparam name="TEntity">The entity that holds it.</typeparam>
    /// <typeparam name="TValue">The value object, or the list of them.</typeparam>
    /// <param name="entity">The entity's builder, in its <c>Configure</c>.</param>
    /// <param name="member">What to store: <c>t =&gt; t.LocalWells</c>.</param>
    /// <param name="column">The column's name — one an adopted table already has; the property's name when omitted.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    ///     <para>
    ///         It is EF Core's JSON mapping, so the column is <c>TEXT</c> on SQLite, <c>nvarchar(max)</c> (or <c>json</c>)
    ///         on SQL Server and <c>jsonb</c> on PostgreSQL, and a query can still filter on what is inside:
    ///         <c>Tenant.Where(t =&gt; t.LocalWells!.Provider == provider)</c>. A nullable member is a <c>NULL</c> column
    ///         when it holds nothing.
    ///     </para>
    ///     <para>
    ///         A value object that holds a list must be stored this way: a list has no columns of its own. EF Core builds a
    ///         value object that holds another, or a list, through its members rather than its constructor, so give such
    ///         a holder a private parameterless constructor and <c>init</c> members.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><paramref name="member" /> is not a value object or a list of them.</exception>
    public static EntityTypeBuilder<TEntity> Json<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity, [DynamicallyAccessedMembers(DataTrimming.Entity)] TValue>(
        this EntityTypeBuilder<TEntity> entity, Expression<Func<TEntity, TValue?>> member, string? column = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(member);

        var name = member.Body is MemberExpression { Member: var held }
            ? held.Name
            : throw new ArgumentException("Name a property of the entity: t => t.Settings.", nameof(member));

        if (entity.Metadata.FindComplexProperty(name) is { IsCollection: true } collection)
        {
            entity.ComplexCollection(collection.ClrType, name).ToJson(column ?? name);
            return entity;
        }

        if (typeof(TValue) == typeof(string) || typeof(TValue).IsValueType)
        {
            throw new InvalidOperationException(
                $"'{typeof(TEntity).Name}.{name}' is a {typeof(TValue).Name}, which is a column already; Json stores a value " +
                "object of several members, or a list of them.");
        }

        entity.ComplexProperty(typeof(TValue), name).ToJson(column ?? name);
        return entity;
    }
}
