using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rask.Core.Caching;

namespace Rask.Server.Caching;

/// <summary>
///     Where a server keeps <c>Cached</c> fragments: the app's <see cref="HybridCache" /> when it registered one —
///     <c>AddHybridCache()</c>, before or after <c>AddRask()</c> — and an in-process store bounded by
///     <see cref="CachedOptions" /> otherwise.
/// </summary>
internal static class FragmentStoreRegistration
{
    internal static void Register(IServiceCollection services)
    {
        // Decided at the first fragment, not here: the app may register its HybridCache after AddRask().
        services.TryAddSingleton<IFragmentStore>(static sp =>
        {
            var options = sp.GetService<IOptions<CachedOptions>>()?.Value ?? new CachedOptions();
            return sp.GetService<HybridCache>() is { } hybrid
                ? new HybridFragmentStore(hybrid, options, Clock.TimeProvider)
                : new MemoryFragmentStore(options);
        });
        services.TryAddSingleton<IRequestHeaders, RequestHeaders>();
    }
}
