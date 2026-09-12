using System.Text.RegularExpressions;

namespace Rask.Spa.Hosting;

/// <summary>
///     Decides whether a built asset may be cached for ever, and which paths are assets rather than
///     client-side routes.
/// </summary>
/// <remarks>
///     This is the one judgement in the package that cannot be taken back. A wrong <c>immutable</c>
///     leaves a stale file in every visitor's disk cache for a year, and the only cure is renaming it —
///     so the rules run cheapest-and-safest first and the guess comes last.
///     <para>
///         A Rask WebAssembly bundle follows different rules from a bundler's output, because what
///         guarantees a content hash differs. Only two places in it are hashed: <c>/_rask/a/</c>, whose
///         names are content hashes by construction, and <c>/_framework/</c> files the SDK fingerprinted
///         (<c>dotnet.7a8b9c2d3e.js</c>) — the SDK's default output there is unfingerprinted
///         (<c>dotnet.js</c>) and has to revalidate. Everything else in <c>wwwroot</c> is authored by
///         hand, so neither the bundler's <c>/assets/</c> guarantee nor the filename guess applies.
///     </para>
/// </remarks>
internal static class SpaCacheClassification
{
    /// <summary>The .NET runtime's files in a WebAssembly bundle.</summary>
    internal const string FrameworkPrefix = "/_framework/";

    /// <summary>Rask's content-addressed scoped CSS and JavaScript in a WebAssembly bundle.</summary>
    internal const string ScopedAssetPrefix = "/_rask/a/";

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
    ///     The .NET SDK's fingerprint: <c>&lt;stem&gt;.&lt;10+ lowercase alphanumerics&gt;.&lt;ext&gt;</c>.
    ///     Ten characters and lowercase, so <c>System.IO.Pipelines.wasm</c> is not mistaken for one.
    /// </summary>
    private static readonly Regex _sdkFingerprint = new(
        @"\.[0-9a-z]{10,}\.[^.]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Whether the asset at <paramref name="requestPath" /> may be marked immutable.
    /// </summary>
    public static bool IsImmutable(
        string? requestPath,
        string fileName,
        SpaHostingOptions options,
        bool wasm = false)
    {
        // The entry document is never immutable, and this is checked first so no later rule can
        // reach it. Freezing index.html strands every visitor on the deployment they first saw,
        // including the script tags naming the bundle — nothing else about a deploy would work.
        if (string.Equals(fileName, options.IndexFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (wasm)
        {
            if (HasPrefix(requestPath, ScopedAssetPrefix))
            {
                return true;
            }

            if (HasPrefix(requestPath, FrameworkPrefix))
            {
                return _sdkFingerprint.IsMatch(fileName);
            }
        }

        // The bundler's own guarantee: everything it writes under this prefix is content-hashed.
        // A guarantee beats a filename heuristic, so it is consulted before one.
        foreach (var prefix in ConfiguredPrefixes(options, wasm))
        {
            if (HasPrefix(requestPath, prefix))
            {
                return true;
            }
        }

        if (wasm)
        {
            return false;
        }

        // Last, and only for bundlers that hash at the dist root (Angular's main-ABCD1234.js).
        var match = _fingerprint.Match(fileName);
        return match.Success && match.Groups[1].Value.Any(char.IsDigit);
    }

    /// <summary>
    ///     Whether a request path can only ever name a file — so a miss there is a 404, never the index
    ///     document.
    /// </summary>
    public static bool IsAssetPath(string? requestPath, SpaHostingOptions options, bool wasm = false)
    {
        if (wasm && (HasPrefix(requestPath, FrameworkPrefix) || HasPrefix(requestPath, ScopedAssetPrefix)))
        {
            return true;
        }

        foreach (var prefix in ConfiguredPrefixes(options, wasm))
        {
            if (HasPrefix(requestPath, prefix))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The prefixes the app configured. In a WebAssembly bundle the untouched default is left out: it
    ///     names Vite's hashed directory, and an <c>assets</c> folder a Rask app keeps in <c>wwwroot</c> is
    ///     hand-written files that must revalidate.
    /// </summary>
    private static IEnumerable<string> ConfiguredPrefixes(SpaHostingOptions options, bool wasm) =>
        wasm && options.ImmutablePathPrefixes is [SpaHostingOptions.DefaultImmutablePathPrefix]
            ? []
            : options.ImmutablePathPrefixes;

    private static bool HasPrefix(string? path, string prefix) =>
        !string.IsNullOrEmpty(path) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}
