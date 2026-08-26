using System.Text.RegularExpressions;

namespace Rask.Spa.Hosting;

/// <summary>
///     Decides whether a built asset may be cached for ever.
/// </summary>
/// <remarks>
///     This is the one judgement in the package that cannot be taken back. A wrong <c>immutable</c>
///     leaves a stale file in every visitor's disk cache for a year, and the only cure is renaming it —
///     so the rules run cheapest-and-safest first and the guess comes last.
///     <para>
///         <c>Rask.Wasm.Hosting</c>'s rule is deliberately not reused: it matches
///         <c>dotnet.7a8b9c2d3e.js</c> (dot-separated, lowercase hex, ten or more) and would miss every
///         bundler here, because Vite emits <c>index-DkK9xYz1.js</c> — dash-separated and mixed case.
///     </para>
/// </remarks>
internal static class SpaCacheClassification
{
    /// <summary>
    ///     A trailing <c>-hash.ext</c> or <c>.hash.ext</c> segment, captured so it can be checked for a
    ///     digit.
    /// </summary>
    /// <remarks>
    ///     Eight characters minimum, and at least one digit in the captured group. Length alone is not
    ///     enough: <c>some-longcomponent.js</c> clears it on length and would be frozen for a year,
    ///     while every real content hash any of these bundlers emits contains digits.
    /// </remarks>
    private static readonly Regex _fingerprint = new(
        @"[-.]([A-Za-z0-9_-]{8,})\.[^.]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Whether the asset at <paramref name="requestPath" /> may be marked immutable.
    /// </summary>
    public static bool IsImmutable(string? requestPath, string fileName, SpaHostingOptions options)
    {
        // The entry document is never immutable, and this is checked first so no later rule can
        // reach it. Freezing index.html strands every visitor on the deployment they first saw,
        // including the script tags naming the bundle — nothing else about a deploy would work.
        if (string.Equals(fileName, options.IndexFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The bundler's own guarantee: everything it writes under this prefix is content-hashed.
        // A guarantee beats a filename heuristic, so it is consulted before one.
        if (!string.IsNullOrEmpty(requestPath))
        {
            foreach (var prefix in options.ImmutablePathPrefixes)
            {
                if (requestPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Last, and only for bundlers that hash at the dist root (Angular's main-ABCD1234.js).
        var match = _fingerprint.Match(fileName);
        return match.Success && match.Groups[1].Value.Any(char.IsDigit);
    }
}
