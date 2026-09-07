using Microsoft.CodeAnalysis;

namespace Rask.Example.Playground.Compiler;

/// <summary>
///     Builds the Roslyn metadata-reference set for the in-browser compiler from the REFERENCE
///     ASSEMBLIES this app shipped as data under <c>refs/</c>.
/// </summary>
/// <remarks>
///     <para>
///         It used to download the app's own implementation assemblies out of <c>_framework/</c>, read
///         off the runtime's boot config. That worked, and it is why the whole app had to be
///         <c>PublishTrimmed=false</c>: trimming strips members Roslyn must see, so the app could not be
///         trimmed without breaking the one thing it exists to do — 53 MB of assemblies, on every page.
///     </para>
///     <para>
///         The staged set is <c>@(ReferencePathWithRefAssemblies)</c>: exactly what the C# compiler saw
///         when it built this project, metadata only, with Roslyn's own assemblies excluded because a
///         snippet never references the compiler. 8.8 MB against 53 MB, and it is the more correct set
///         besides — <c>_framework</c> offered whatever happened to survive into the bundle, which
///         changes with unrelated build settings.
///     </para>
///     <para>
///         WASM has no real filesystem, so <c>CreateFromFile</c> is unusable; each file is fetched and
///         wrapped with <c>MetadataReference.CreateFromImage</c>. Fetched once and cached.
///     </para>
/// </remarks>
public sealed class WasmReferenceLoader
{
    /// <summary>The manifest the build writes beside the staged assemblies, one file name per line.</summary>
    /// <remarks>
    ///     A manifest rather than a directory listing, because a browser cannot list one. Written from
    ///     the same MSBuild item the copy used, so it cannot describe a set the copy did not produce.
    /// </remarks>
    private const string ManifestPath = "refs.txt";

    private const string ReferenceDirectory = "refs/";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MetadataReference>? _cache;

    public WasmReferenceLoader(HttpClient http) => _http = http;

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

            var manifest = await _http.GetStringAsync(ManifestPath, cancellationToken).ConfigureAwait(false);
            var names = manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (names.Length == 0)
            {
                // Don't cache an empty set — a manifest that momentarily fails to load must be retryable
                // on the next Run rather than wedging the compiler into "everything is missing" for the
                // whole session.
                throw new InvalidOperationException(
                    $"'{ManifestPath}' listed no reference assemblies, so there is nothing to compile "
                    + "against. It is written by _PlaygroundStageReferenceAssemblies at build time.");
            }

            var urls = names.Select(name => ReferenceDirectory + name).ToArray();

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

            _cache = references;
            return references;
        }
        finally
        {
            _gate.Release();
        }
    }
}
