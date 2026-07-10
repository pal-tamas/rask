using System.Net;
using System.Net.Http.Headers;

namespace Rask.Native;

/// <summary>
///     Serves an in-process <see cref="HttpClient" />'s fetches from the app's bundled assets instead of the
///     network, through the same <see cref="NativeOriginAssets" /> table the WebView interceptor uses — so a
///     Native + Local app's data-driven pages (e.g. <c>data/*.json</c>) work offline on-device.
///     Register the demo <see cref="HttpClient" /> over this handler with <c>BaseAddress</c> = the app
///     origin, so a relative fetch like <c>data/posts-1.json</c> resolves against the bundled assets.
///     Anything not resolved → <see cref="HttpStatusCode.NotFound" />.
/// </summary>
/// <param name="readStaticFile">
///     Reads a static file by its origin-relative key; see <see cref="NativeOriginAssets.Resolve" />.
/// </param>
public sealed class NativeAssetHttpHandler(Func<string, byte[]?> readStaticFile) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = request.RequestUri?.AbsolutePath ?? "/";
        if (NativeOriginAssets.Resolve(path, readStaticFile) is { } asset)
        {
            var ok = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(asset.Body)
            };
            ok.Content.Headers.ContentType = new MediaTypeHeaderValue(asset.ContentType);
            return Task.FromResult(ok);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
    }
}
