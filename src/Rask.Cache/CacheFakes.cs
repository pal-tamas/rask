namespace Rask.Caching;

/// <summary>A test's stand-in for the cache: <c>using var cache = Cache.Fake();</c>.</summary>
public static class CacheFakes
{
    extension(Cache)
    {
        /// <summary>
        ///     Takes the place of the cache for this test — an in-memory one that really stores, so the
        ///     code under test behaves as it would in production — until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var cache = Cache.Fake();
        ///
        ///     await page.Visit("/products");
        ///     await page.Visit("/products");
        ///
        ///     cache.Loaded("products").Once();   // the second visit was served from the cache
        ///     </code>
        ///     <para>
        ///         Expiry is real and reads the app's clock, so <c>Clock.Fake</c> plus <c>Advance</c> proves
        ///         a value is reloaded once it is stale. Scoped to the test's own flow, so tests running in
        ///         parallel never see each other's keys. It stands in front of <c>Cache.Remember</c>; a
        ///         class that takes <see cref="ICache" /> in its constructor is handed whatever the
        ///         container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;ICache&gt;(cache)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static CacheFake Fake() => new();
    }
}
