using Rask.Data;
using Rask.Query;

namespace Rask.Site.DataDemo;

/// <summary>
///     Refreshes the queries about whatever a save wrote: <c>Note.CreateAsync(model)</c> refetches every
///     <c>QueryKey.For&lt;Note&gt;(…)</c> query on screen, so the list beside the form updates with nothing written
///     at the call site.
/// </summary>
/// <remarks>
///     A Rask server app gets this from the host. Rask.Data has no browser wiring yet (#1132), so this app registers
///     the same few lines itself, and opens <c>Db.UseScope</c> around its writes so the save can find it.
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
