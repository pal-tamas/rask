using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Rask.Wasm.Hosting;

// When the consumer's WASM publish step emits .br / .gz siblings next to each framework
// asset (e.g. dotnet.native.wasm + dotnet.native.wasm.br + dotnet.native.wasm.gz —
// what the WASM SDK does when <CompressionEnabled>true</CompressionEnabled> is set),
// serve the sibling directly with the matching Content-Encoding header. Pre-compressed
// files cost zero request-time CPU and let an upstream CDN cache the encoded bytes —
// avoiding the per-request brotli pass that UseResponseCompression otherwise performs.
//
// Falls through to the next middleware (UseStaticFiles / UseResponseCompression) when:
//   - no sibling exists on disk for the requested path,
//   - the client didn't advertise an Accept-Encoding we can serve,
//   - the request method isn't GET or HEAD.
//
// This is intentionally a thin shim: it rewrites Path to the sibling, sets the encoding
// headers + Vary, and lets the existing UseStaticFiles configuration (MIME types, cache
// classification from PR7) handle the actual byte serving.
internal sealed class PrecompressedFileMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IFileProvider _fileProvider;

    public PrecompressedFileMiddleware(RequestDelegate next, IFileProvider fileProvider)
    {
        _next = next;
        _fileProvider = fileProvider;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            return _next(context);
        }

        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            return _next(context);
        }

        var accept = context.Request.Headers.AcceptEncoding;
        if (accept.Count == 0)
        {
            return _next(context);
        }

        // Prefer brotli — denser, and any modern browser that supports gzip also supports br.
        var encoding = SelectEncoding(accept);
        if (encoding is null)
        {
            return _next(context);
        }

        var siblingPath = path + (encoding == "br" ? ".br" : ".gz");
        var sibling = _fileProvider.GetFileInfo(siblingPath);
        if (!sibling.Exists || sibling.IsDirectory)
        {
            return _next(context);
        }

        // Rewrite the request to point at the sibling so UseStaticFiles serves it. The
        // original path is preserved on the URL the client sees (no redirect); only the
        // file-provider lookup target changes.
        context.Request.Path = siblingPath;
        context.Response.Headers.ContentEncoding = encoding;
        context.Response.Headers[HeaderNames.Vary] = HeaderNames.AcceptEncoding;
        return _next(context);
    }

    private static string? SelectEncoding(Microsoft.Extensions.Primitives.StringValues acceptHeader)
    {
        // Header parsing is intentionally minimal: a substring contains-check is enough for
        // the common "gzip, deflate, br" / "br;q=1.0, gzip;q=0.9" shapes. Doesn't honor
        // q=0 explicit refusals — pathological case for a framework-asset host.
        var preferBr = false;
        var preferGz = false;
        foreach (var header in acceptHeader)
        {
            if (header is null) continue;
            if (!preferBr && header.Contains("br", StringComparison.OrdinalIgnoreCase))
            {
                preferBr = true;
            }

            if (!preferGz && header.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                preferGz = true;
            }
        }

        if (preferBr) return "br";
        if (preferGz) return "gzip";
        return null;
    }
}
