using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Rask;

/// <summary>
/// Keys <see cref="RaskAppDbContext" />'s model on what it maps besides its type: the batteries that are on, and the
/// migrations an existing battery table is kept for.
/// </summary>
/// <remarks>
/// EF Core caches a model per context TYPE for the life of the process, so without this the first app (or test) to
/// build one would pin its battery switches on every other.
/// </remarks>
internal sealed class RaskAppModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context is RaskAppDbContext app
            ? (context.GetType(), designTime, app.ModelShape)
            : (context.GetType(), designTime);
    }
}
