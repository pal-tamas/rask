namespace Rask.Data;

/// <summary>
///     Told which entity types a save wrote, once that save is durable — for whatever on the current
///     session's screen shows them and should refetch.
/// </summary>
/// <remarks>
///     <para>
///         A Rask app gets one for free: the host registers an implementation that invalidates every
///         <c>Rask.Query</c> query about those types — <c>QueryKey.For&lt;Person&gt;(…)</c>, or anything a
///         command's <c>[Invalidates(typeof(Person))]</c> would reach — so <c>Person.CreateAsync(model)</c>
///         refreshes the list beside the form without anyone writing the invalidation. It lives here rather
///         than in <c>Rask.Query</c> because Rask.Data must not reference the front-end packages.
///     </para>
///     <para>
///         Resolved from the scope <see cref="Db.UseScope" /> opened — the live session's — and <b>scoped</b>
///         for the same reason <see cref="IPrincipalSource" /> is: it belongs to one session. A save with no
///         such scope (a background job, a hosted service) tells nobody, which is right: no screen is
///         waiting on it here, and another session's cache is not this save's to touch. A save in a plain HTTP
///         request reaches that request's own, empty cache — the same nobody.
///     </para>
///     <para>
///         Called after the save commits: straight after <c>SaveChanges</c>, or — inside an explicit
///         <c>BeginTransactionAsync</c> — when that transaction commits, never on a rollback. A child entity is
///         reported as the aggregate that owns it, since that is what a screen asks for.
///     </para>
/// </remarks>
public interface IDataChanges
{
    /// <summary>A save committed, writing rows of these entity types.</summary>
    /// <param name="entityTypes">Each aggregate (or plain entity) type the save wrote, once.</param>
    void Saved(IReadOnlyCollection<Type> entityTypes);
}
