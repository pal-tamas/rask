using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rask.Core.Caching;
using Rask.Hosting.Shared;

namespace Rask.Server.Caching;

/// <summary>
///     Where a server keeps <c>Cached</c> fragments: the app's <see cref="HybridCache" /> when it registered one —
///     <c>AddHybridCache()</c>, before or after <c>AddRask()</c> — and an in-process store bounded by
///     <see cref="CachedOptions" /> otherwise. The options read <c>Rask:Cached</c> first, and a
///     <c>services.Configure&lt;CachedOptions&gt;(…)</c> after <c>AddRask()</c> wins over it.
/// </summary>
internal static class FragmentStoreRegistration
{
    internal static void Register(IServiceCollection services)
    {
        services.AddRaskOptions<CachedOptions>("Rask:Cached", static (section, o) => section.Bind(o), configure: null,
            Validate);

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

    private static void Validate(CachedOptions options)
    {
        if (options.SizeLimit <= 0)
        {
            throw new InvalidOperationException("SizeLimit must be positive.");
        }

        if (options.MaxEntrySize <= 0)
        {
            throw new InvalidOperationException("MaxEntrySize must be positive.");
        }

        if (options.DefaultLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("DefaultLifetime must be positive.");
        }
    }
}
