using Rask.Core.Components;

namespace Rask;

/// <summary>
///     What a cached fragment varies by, in words: <c>Cached.Name("orders").VaryByQuery("status", "page")</c>,
///     <c>.VaryByHeader("User-Agent")</c>, <c>.Tags("catalog", "prices")</c>.
/// </summary>
public static class CachedSteps
{
    extension(Cached fragment)
    {
        /// <summary>Varies by these query values: a fragment of its own for each combination of them.</summary>
        /// <param name="name">A query value it varies by.</param>
        /// <param name="more">The others.</param>
        /// <returns>The fragment.</returns>
        public Cached VaryByQuery(string name, params string[] more) => fragment.VaryByQuery([name, .. more]);

        /// <summary>Varies by these request headers. Drawn live where there is no request to read — in the browser.</summary>
        /// <param name="name">A header it varies by.</param>
        /// <param name="more">The others.</param>
        /// <returns>The fragment.</returns>
        public Cached VaryByHeader(string name, params string[] more) => fragment.VaryByHeader([name, .. more]);

        /// <summary>Tags to forget it by, besides its name: <c>await CachedFragments.Forget("catalog")</c>.</summary>
        /// <param name="tag">A tag.</param>
        /// <param name="more">The others.</param>
        /// <returns>The fragment.</returns>
        public Cached Tags(string tag, params string[] more) => fragment.Tags([tag, .. more]);
    }
}
