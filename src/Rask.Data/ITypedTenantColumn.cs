using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Data;

/// <summary>
///     A <c>TenantId</c> column kept as the tenant's own typed id — <c>TenantId? TenantId</c>, or
///     <c>PerTenant&lt;TenantId&gt;</c> — that <see cref="TenantColumn" /> filters by and stamps.
/// </summary>
/// <remarks>
///     Closed over the id and the value it holds where the tenant is registered
///     (<see cref="CurrentRows.UseTenant{TAggregate,TRead,TKey,TValue}" />, <see cref="TypedTenantColumn{TKey,TValue}" />), so every reader the filter calls is a
///     method of a closed type the trimmer can follow — never one made with <c>MakeGenericMethod</c>.
/// </remarks>
internal interface ITypedTenantColumn
{
    /// <summary>The typed id.</summary>
    Type Key { get; }

    /// <summary>The column's type: the typed id, nullable.</summary>
    Type Column { get; }

    /// <summary>The value the id holds, nullable: the type a battery table that follows the app's column keeps.</summary>
    Type Value { get; }

    /// <summary><c>EF.Property&lt;TKey?&gt;(entity, "TenantId")</c>.</summary>
    MethodInfo Stored { get; }

    /// <summary>The tenant in flight as the typed id, read through <paramref name="scope" /> per query.</summary>
    /// <param name="scope">The context, as an <see cref="ITenantScoped" />.</param>
    /// <param name="table">The table's name, for the sentence a tenant that is not a number throws.</param>
    Expression Current(Expression scope, Expression table);

    /// <summary>What a row of <paramref name="table" /> is stamped with for <paramref name="tenant" />.</summary>
    /// <exception cref="InvalidOperationException">The id holds a number and the tenant is not one.</exception>
    object ValueFor(Guid tenant, string table);

    /// <summary>A stored id as the <see cref="Guid" /> the framework carries, or null when it is not this id.</summary>
    Guid? Carried(object stored);
}
