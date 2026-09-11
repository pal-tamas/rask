using Rask.Core.Globalization;

namespace Rask.Server.Prerender;

/// <summary>
///     What a stored copy of a page is a copy of: a route path, in a language.
/// </summary>
/// <param name="Path">The route path, with any <c>PathBase</c> removed.</param>
/// <param name="Culture">The formatting culture's name, or empty when the app configured no languages.</param>
/// <param name="UICulture">The UI culture's name, or empty when the app configured no languages.</param>
/// <remarks>
///     Deliberately nothing else. Everything a stored copy may vary on has to be in the key, and
///     everything in the key multiplies how many copies there can be — so a request that could vary on
///     more (a query string, a signed-in user whose page reads them) is served live rather than keyed.
/// </remarks>
internal readonly record struct PageCacheKey(string Path, string Culture, string UICulture)
{
    /// <summary>The key for <paramref name="path" /> in <paramref name="culture" />.</summary>
    internal static PageCacheKey For(string path, CultureNegotiation? culture) =>
        culture is { } negotiated
            ? new PageCacheKey(path, negotiated.Culture.Name, negotiated.UICulture.Name)
            : new PageCacheKey(path, string.Empty, string.Empty);
}
