using Rask.Data;
using Rask.Query;

namespace Rask;

/// <summary>
///     Refreshes the session's queries about whatever a save wrote: <c>Person.CreateAsync(model)</c>
///     refetches every <c>QueryKey.For&lt;Person&gt;(…)</c> query on this session's screen.
/// </summary>
/// <remarks>
///     A prefix invalidation per type, exactly <c>QueryClient.Invalidate&lt;Person&gt;()</c>: it reaches a key
///     built with <c>QueryKey.For&lt;Person&gt;</c> and one a command reaches with
///     <c>[Invalidates(typeof(Person))]</c>, and nothing else. A message query about people —
///     <c>GetPeople</c> — is keyed by its own type, so it still needs that attribute on the command; the
///     write cannot know which messages read the table. Scoped: this session's cache, never another's.
/// </remarks>
internal sealed class QueryDataChanges(IQueryClient queries) : IDataChanges
{
    public void Saved(IReadOnlyCollection<Type> entityTypes)
    {
        foreach (var type in entityTypes)
        {
            queries.Invalidate(type);
        }
    }
}
