namespace Rask.Query;

/// <summary>
///     Names the entities this query reads, so it refetches when anything in the process writes one — another
///     visitor's command, a background job — and not only when this session does.
/// </summary>
/// <remarks>
///     <para>
///         The read-side mirror of <see cref="InvalidatesAttribute" />, and declared for the same reason: a query
///         message is keyed by <em>itself</em>, and a write cannot know which message types read the table.
///         <code>
/// [Live(typeof(Order))]
/// public sealed record GetOrders(int Page) : IQuery&lt;IReadOnlyList&lt;OrderRead&gt;&gt;;
///         </code>
///         Nothing is said at the call site: <c>QueryClient.Query(new GetOrders(Page))</c> is live wherever it is
///         written, so <c>Render</c> keeps saying what to show rather than how it is kept fresh.
///     </para>
///     <para>
///         A query keyed by the entity itself — <c>QueryKey.For&lt;Order&gt;(…)</c>, a Rask.Data read face — needs
///         no attribute: the key already names it.
///     </para>
///     <para>
///         <b>It takes two.</b> The entity must also announce its saves, with <c>public const Broadcasts Broadcast
///         = Broadcasts.OnCommit;</c>, or nothing is published for it to hear. That way a table no page watches
///         costs nothing, and turning it on is one declaration on the model — never a change to a page.
///     </para>
///     <para>
///         <b>Who may see it.</b> Nobody new. The query already decided who may read the data, so a refetch of it
///         is admitted by the same rule: there is no second policy to write, and no way to be told about a row you
///         could not have queried.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public sealed class LiveAttribute : Attribute
{
    /// <summary>Names the entities this query reads.</summary>
    /// <param name="entities">The entity types whose saves should refetch this query.</param>
    public LiveAttribute(params Type[] entities) => Entities = entities ?? [];

    /// <summary>The entity types whose saves refetch this query.</summary>
    public IReadOnlyList<Type> Entities { get; }
}
