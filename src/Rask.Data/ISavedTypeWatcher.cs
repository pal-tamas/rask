namespace Rask.Data;

/// <summary>
///     Told, process-wide, when a save of a type it watches commits — whichever work saved it: a page, a request,
///     a background job.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="IDataChanges" /> is the session's own and hears nothing a background job writes; this is the
///         host's, a singleton beside <c>DataChangesInterceptor</c>, for what the host keeps across sessions — the
///         users a session revalidator keeps, dropped when a user is saved.
///     </para>
///     <para>
///         Called after the commit, as <see cref="IDataChanges.Saved" /> is, never on a rollback; a child entity is
///         reported as its aggregate.
///     </para>
/// </remarks>
internal interface ISavedTypeWatcher
{
    /// <summary>Whether a save of <paramref name="aggregate" /> is one this wants to hear of.</summary>
    /// <param name="aggregate">The aggregate (or plain entity) type a save wrote.</param>
    /// <returns><see langword="true" /> to be told when it commits.</returns>
    bool Watches(Type aggregate);

    /// <summary>A save committed, writing rows of these types — at least one of them one this watches.</summary>
    /// <param name="aggregates">Each aggregate (or plain entity) type the save wrote, once.</param>
    void Saved(IReadOnlyCollection<Type> aggregates);
}
