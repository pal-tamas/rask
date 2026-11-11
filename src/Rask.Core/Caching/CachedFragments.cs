using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Caching;

namespace Rask;

/// <summary>
///     Forgets cached fragments, so the next request draws them afresh — after a write that changes what they show:
///     <c>await CachedFragments.Forget("catalog");</c>
/// </summary>
/// <remarks>
///     A fragment can be forgotten by its name (<c>Cached.Name("sales-summary")</c>) or by any of its tags
///     (<c>.Tags("catalog")</c>), and is forgotten for every user, tenant and value it varies by. With a
///     <c>HybridCache</c> registered this is its <c>RemoveByTagAsync</c>, so every server sharing it forgets at once.
/// </remarks>
public static class CachedFragments
{
    /// <summary>Forgets every fragment named <paramref name="nameOrTag" /> or tagged with it.</summary>
    /// <param name="nameOrTag">A fragment's name, or one of its tags.</param>
    /// <param name="cancellationToken">Stops the removal; the ambient work's token when left out.</param>
    public static async Task Forget(string nameOrTag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameOrTag);
        var store = Ambient.Services?.GetService<IFragmentStore>() ?? MemoryFragmentStore.Shared;
        await store.Forget(FragmentKeys.Tag(nameOrTag), Ambient.Or(cancellationToken)).ConfigureAwait(false);
    }
}
