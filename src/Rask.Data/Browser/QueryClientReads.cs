using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Rask.Querying;

namespace Rask.Data;

// One file, two hosts, like QueryDataChanges: Rask.Data's browser build compiles it, and the server host links it in,
// because Rask.Data's server build does not reference Rask.Query. The cache makes the fetch's token ambient, so the
// awaited query is cancelled with the fetch.

/// <summary>
///     Caches a Data read in the session's query cache by handing it the query itself —
///     <c>QueryClient.Query("active", Person.Where(p =&gt; p.Active))</c>.
/// </summary>
public static class QueryClientReads
{
    // DataTrimming.Entity, repeated: the server host compiles this file into its own assembly, where Rask.Data's
    // internals are out of reach.
    private const DynamicallyAccessedMemberTypes Entity =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties
        | DynamicallyAccessedMemberTypes.Interfaces;

    extension(QueryClient)
    {
        /// <summary>
        ///     The query that caches the rows <paramref name="rows" /> reads under <paramref name="key" />; the cache
        ///     runs it, and cancels it when nothing shows it any more.
        /// </summary>
        /// <param name="key">A name unique to this data within the session; a string converts to one.</param>
        /// <param name="rows">The read, not yet run.</param>
        /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
        /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
        /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
        public static Query<List<TEntity>> Query<[DynamicallyAccessedMembers(Entity)] TEntity>(
            QueryKey key,
            ModelQuery<TEntity> rows,
            QueryOptions? options = null,
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return QueryClient.Query(key, async () => await rows.ConfigureAwait(false), options, callerFile, callerLine);
        }

        /// <summary>
        ///     The query that caches the projected rows <paramref name="rows" /> reads under <paramref name="key" />.
        /// </summary>
        /// <param name="key">A name unique to this data within the session; a string converts to one.</param>
        /// <param name="rows">The projected read, not yet run.</param>
        /// <param name="options">Freshness and lifetime; TanStack's defaults when omitted.</param>
        /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
        /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
        public static Query<List<TResult>> Query<[DynamicallyAccessedMembers(Entity)] TEntity, TResult>(
            QueryKey key,
            Projection<TEntity, TResult> rows,
            QueryOptions? options = null,
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return QueryClient.Query(key, async () => await rows.ConfigureAwait(false), options, callerFile, callerLine);
        }
        /// <summary>
        ///     The query that caches <paramref name="rows" />' rows for this render's <paramref name="input" />, under
        ///     <c>[..prefix, input]</c> — <c>QueryClient.Query(QueryKey.For&lt;Person&gt;(), _search, s =&gt; Person.Search(s))</c>.
        /// </summary>
        /// <param name="prefix">What the data is about — <c>QueryKey.For&lt;Person&gt;()</c>, or a name.</param>
        /// <param name="input">This render's input.</param>
        /// <param name="rows">The read for one input, not yet run.</param>
        /// <param name="options">Freshness and retry; TanStack's defaults when omitted.</param>
        /// <param name="callerFile">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
        /// <param name="callerLine">Supplied by the compiler; identifies this call inside <c>Render</c>.</param>
        public static Query<List<TEntity>> Query<TInput, [DynamicallyAccessedMembers(Entity)] TEntity>(
            QueryKey prefix,
            TInput input,
            Func<TInput, ModelQuery<TEntity>> rows,
            QueryOptions? options = null,
            [CallerFilePath] string callerFile = "",
            [CallerLineNumber] int callerLine = 0)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return QueryClient.Query(prefix, input, async (value, _) => await rows(value).ConfigureAwait(false), options, callerFile, callerLine);
        }

        /// <summary>
        ///     The query that caches <paramref name="rows" />' rows for whatever <paramref name="input" /> reads now,
        ///     for a field or a constructor.
        /// </summary>
        /// <param name="prefix">What the data is about — <c>QueryKey.For&lt;Person&gt;()</c>, or a name.</param>
        /// <param name="input">Reads the current input.</param>
        /// <param name="rows">The read for one input, not yet run.</param>
        /// <param name="options">Freshness and retry; TanStack's defaults when omitted.</param>
        public static Query<List<TEntity>> Query<TInput, [DynamicallyAccessedMembers(Entity)] TEntity>(
            QueryKey prefix,
            Func<TInput> input,
            Func<TInput, ModelQuery<TEntity>> rows,
            QueryOptions? options = null)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return QueryClient.Query(prefix, input, async (value, _) => await rows(value).ConfigureAwait(false), options);
        }
    }
}
