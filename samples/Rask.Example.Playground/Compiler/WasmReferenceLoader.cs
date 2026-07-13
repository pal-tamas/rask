using Microsoft.CodeAnalysis;
using Microsoft.JSInterop;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     Builds the Roslyn metadata-reference set for the in-browser compiler by downloading the assemblies
///     this app already shipped under <c>_framework/</c>. WASM has no real filesystem, so
///     <c>MetadataReference.CreateFromFile</c> is unusable; instead a tiny JS shim reads the .NET runtime's
///     own boot config for the exact (fingerprinted) assembly URLs, and each is fetched and wrapped via
///     <c>MetadataReference.CreateFromImage</c>. Assemblies ship as plain PE here (the app sets
///     <c>WasmEnableWebcil=false</c>) so Roslyn can read them. The set is fetched once and cached — it's
///     several MB, so subsequent compiles reuse it.
/// </summary>
public sealed class WasmReferenceLoader
{
    // Sibling of PlaygroundView.js: `export function frameworkAssemblyUrls()`. Scoped-JS modules are
    // registered under Rask.{ComponentName}, so the invoke id is Rask.PlaygroundView.frameworkAssemblyUrls.
    private const string UrlsExport = "Rask.PlaygroundView.frameworkAssemblyUrls";

    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MetadataReference>? _cache;

    public WasmReferenceLoader(IJSRuntime js, HttpClient http)
    {
        _js = js;
        _http = http;
    }

    /// <summary>Count of references in the last successful load — surfaced in the UI as a sanity signal.</summary>
    public int LoadedCount { get; private set; }

    public async Task<IReadOnlyList<MetadataReference>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache is not null)
            {
                return _cache;
            }

            var urls = await _js.InvokeAsync<string[]>(UrlsExport, cancellationToken).ConfigureAwait(false);
            if (urls is null || urls.Length == 0)
            {
                // Don't cache an empty set — a runtime that momentarily reports no assemblies, or a
                // resource-shape the JS collector didn't recognise, must be retryable on the next Run
                // rather than wedging the compiler into "everything is missing" for the whole session.
                throw new InvalidOperationException(
                    "The .NET runtime reported no framework assemblies to use as compiler references.");
            }

            // Fetch concurrently — the URLs are independent and this download is the slow part of the first
            // compile. Task.WhenAll surfaces the first failure, so a transient error throws WITHOUT caching
            // (the next Run retries) instead of silently caching a partial set that breaks every compile.
            var images = await Task.WhenAll(
                urls.Select(url => _http.GetByteArrayAsync(url, cancellationToken))).ConfigureAwait(false);

            var references = new List<MetadataReference>(images.Length);
            foreach (var image in images)
            {
                references.Add(MetadataReference.CreateFromImage(image));
            }

            LoadedCount = references.Count;
            _cache = references;
            return references;
        }
        finally
        {
            _gate.Release();
        }
    }
}
