using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Caching;
using Rask.Server.Caching;

namespace Rask.Server.Tests;

// Cached fragments in the app's own HybridCache: what it cannot express (sliding expiry) carried in the stored value.
public sealed class HybridFragmentStoreTests
{
    private readonly FakeTime _time = new();
    private readonly DictionaryHybridCache _cache = new();

    [Fact]
    public async Task A_stored_fragment_comes_back_and_a_missing_one_is_not_written_by_the_read()
    {
        var store = new HybridFragmentStore(_cache, new CachedOptions(), _time);

        await store.Set("k", "<p>sales</p>", new FragmentLifetime(TimeSpan.FromMinutes(5), null), ["rask:cached:sales"]);
        var found = await store.Get("k");
        var missing = await store.Get("other");

        Assert.Equal("<p>sales</p>", found);
        Assert.Null(missing);
        Assert.Single(_cache.Entries);
    }

    [Fact]
    public async Task The_absolute_deadline_holds_even_when_the_cache_would_keep_it_longer()
    {
        var store = new HybridFragmentStore(_cache, new CachedOptions(), _time);
        await store.Set("k", "<p>sales</p>", new FragmentLifetime(TimeSpan.FromMinutes(5), null), []);

        _time.Advance(TimeSpan.FromMinutes(6));
        var found = await store.Get("k");

        Assert.Null(found);
    }

    [Fact]
    public async Task A_sliding_fragment_is_moved_on_when_half_its_window_is_spent_and_never_past_its_deadline()
    {
        var store = new HybridFragmentStore(_cache, new CachedOptions(), _time);
        await store.Set("k", "<p>sales</p>", new FragmentLifetime(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(4)), ["t"]);
        var writesAfterSet = _cache.Writes;

        _time.Advance(TimeSpan.FromMinutes(1));
        await store.Get("k");
        var writesBeforeHalf = _cache.Writes;
        _time.Advance(TimeSpan.FromMinutes(2));
        await store.Get("k");

        Assert.Equal(writesAfterSet, writesBeforeHalf);
        Assert.Equal(writesAfterSet + 1, _cache.Writes);
        Assert.Equal(TimeSpan.FromMinutes(4), _cache.Entries["k"].Expiration);
        Assert.Equal(["t"], _cache.Entries["k"].Tags);
    }

    [Fact]
    public async Task Forgetting_a_tag_is_the_caches_remove_by_tag()
    {
        var store = new HybridFragmentStore(_cache, new CachedOptions(), _time);
        await store.Set("a", "<p>a</p>", new FragmentLifetime(TimeSpan.FromMinutes(5), null), ["rask:cached:catalog"]);
        await store.Set("b", "<p>b</p>", new FragmentLifetime(TimeSpan.FromMinutes(5), null), ["rask:cached:other"]);

        await store.Forget("rask:cached:catalog");

        Assert.Equal(["b"], _cache.Entries.Keys);
    }

    [Fact]
    public async Task A_fragment_over_the_entry_limit_is_not_stored()
    {
        var store = new HybridFragmentStore(_cache, new CachedOptions { MaxEntrySize = 8 }, _time);

        await store.Set("k", "<p>sales</p>", new FragmentLifetime(TimeSpan.FromMinutes(5), null), []);

        Assert.Empty(_cache.Entries);
    }

    [Fact]
    public void A_server_with_a_hybrid_cache_keeps_fragments_in_it_and_one_without_keeps_them_in_process()
    {
        var with = new ServiceCollection().AddSingleton<HybridCache>(_cache);
        FragmentStoreRegistration.Register(with);
        var without = new ServiceCollection();
        FragmentStoreRegistration.Register(without);

        var hybrid = with.BuildServiceProvider().GetRequiredService<IFragmentStore>();
        var memory = without.BuildServiceProvider().GetRequiredService<IFragmentStore>();

        Assert.IsType<HybridFragmentStore>(hybrid);
        Assert.IsType<MemoryFragmentStore>(memory);
    }

    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // Enough of a HybridCache to see what the store asks of it: values by key, their expiration and tags.
    private sealed class DictionaryHybridCache : HybridCache
    {
        public Dictionary<string, (object? Value, TimeSpan? Expiration, string[] Tags)> Entries { get; } = new(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            Entries.TryGetValue(key, out var entry) ? ValueTask.FromResult((T)entry.Value!) : factory(state, cancellationToken);

        public override ValueTask SetAsync<T>(
            string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            Entries[key] = (value, options?.Expiration, [.. tags ?? []]);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Entries.Remove(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        {
            foreach (var key in Entries.Where(e => e.Value.Tags.Contains(tag)).Select(e => e.Key).ToList())
            {
                Entries.Remove(key);
            }

            return ValueTask.CompletedTask;
        }
    }
}
