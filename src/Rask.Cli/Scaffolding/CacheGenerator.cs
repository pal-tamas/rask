namespace Rask.Cli.Scaffolding;

/// <summary>
/// Scaffolds a typed read-through cache accessor under <c>Features/Shared/</c> — or into a feature slice
/// <c>Features/&lt;Feature&gt;/</c> when <c>--feature</c> names one (or an explicit <c>--output</c> dir).
/// </summary>
/// <remarks>
/// The point of the generated class is the pair that is easy to get wrong by hand: one place that owns the
/// cache <i>key</i> and one that owns <i>invalidation</i>. Scattering <c>ICache.GetOrCreateAsync("…")</c>
/// calls with inline string keys is how a cache ends up with a stale entry nobody can find.
/// </remarks>
internal static class CacheGenerator
{
    public static ScaffoldResult Generate(
        ProjectContext project, string baseDirectory, string name, string? feature, string? outputOverride)
    {
        var targetDirectory = Scaffold.TargetDirectory(baseDirectory, outputOverride, Scaffold.FeatureOrShared(feature));
        var file = new ScaffoldFile(Path.Combine(targetDirectory, name + ".cs"), Render(project.NamespaceFor(targetDirectory), name));
        return new ScaffoldResult([file], Notes(name))
        {
            Packages = ["Rask.Cache"],
        };
    }

    /// <summary>Render the cache accessor source. Pure, so it is unit-tested directly.</summary>
    internal static string Render(string @namespace, string name) =>
        $$"""
        using Microsoft.Extensions.Caching.Distributed;

        namespace {{@namespace}};

        /// <summary>
        /// A read-through cache over one expensive read. Owns its key and its expiry in one place, so the
        /// value can be invalidated from anywhere without a caller having to know how the key is built.
        /// </summary>
        public sealed class {{name}}(ICache cache)
        {
            // Version the key rather than mutating it in place: change the suffix when the shape of the
            // cached value changes, and stale entries from the old shape are simply never read again.
            private const string Key = "{{name.ToLowerInvariant()}}:v1";

            private static readonly DistributedCacheEntryOptions Lifetime =
                new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

            /// <summary>Returns the cached value, computing and storing it on a miss.</summary>
            public Task<string> GetAsync(CancellationToken cancellationToken = default) =>
                cache.GetOrCreateAsync(
                    Key,
                    async ct =>
                    {
                        // TODO: the expensive read this cache exists to avoid — a heavy query, an aggregate,
                        // a third-party call. Whatever you return here is what gets stored.
                        await Task.CompletedTask.ConfigureAwait(false);
                        return "TODO";
                    },
                    Lifetime,
                    cancellationToken);

            /// <summary>
            /// Drops the cached value. Call this from the command handler that changes the underlying data —
            /// invalidating at the point of the write is what keeps the cache from serving a stale answer.
            /// </summary>
            public Task InvalidateAsync(CancellationToken cancellationToken = default) =>
                cache.RemoveAsync(Key, cancellationToken);
        }

        """;

    /// <summary>The "register it and create the schema" next-steps printed after scaffolding.</summary>
    internal static string Notes(string name) =>
        $"""
        Next steps:
          1. Register the services in Program.cs (once):
               builder.Services.AddRaskCache<AppDbContext>();   // your DbContext
          2. Map the cache table in your DbContext's OnModelCreating (once):
               modelBuilder.AddRaskCache();
          3. Create the schema:
               rask db add AddCache && rask db update
          4. Register the accessor and inject it:
               builder.Services.AddScoped<{name}>();
          5. Invalidate it wherever the underlying data changes:
               await {name.ToLowerInvariant()}.InvalidateAsync(cancellationToken);
        """;
}
