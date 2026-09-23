namespace Rask.Cqrs;

/// <summary>
///     A save committed, writing rows of one entity. Published process-wide so that a page which did not make the
///     write hears about it — the half <c>IDataChanges</c> cannot do, being the saving session's own.
/// </summary>
/// <remarks>
///     <para>
///         Published for an entity that declares <c>Broadcast = Broadcasts.OnCommit</c>, and consumed by
///         <c>Rask.Query</c>, which refetches the queries that declared they read that entity. An application
///         rarely names it: declaring the const and calling <c>Live()</c> is the whole of it.
///     </para>
///     <para>
///         The entity travels as a name rather than a <see cref="System.Type" /> so the same notification crosses
///         the wire to a WebAssembly front end, where the server's <see cref="System.Type" /> means nothing.
///     </para>
///     <para>
///         <paramref name="At" /> is what lets a listener ignore the change it is replayed when it opens: a
///         subscription starts with the last value published, and a page that has just fetched is already showing
///         it. Everything published afterwards is a change the page has not seen.
///     </para>
/// </remarks>
/// <param name="Entity">The full name of the entity type the save wrote.</param>
/// <param name="At">When the save committed.</param>
public sealed record DataChanged(string Entity, DateTimeOffset At) : INotification;
