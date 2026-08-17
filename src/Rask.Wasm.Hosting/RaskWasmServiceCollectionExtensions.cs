using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Wasm.Hosting;

/// <summary>
///     Registers what a WASM-bundle host needs before <c>UseRask</c> can serve one.
/// </summary>
public static class RaskWasmServiceCollectionExtensions
{
    /// <summary>
    ///     Registers response compression with brotli + gzip providers and the MIME types the
    ///     dotnet WASM AppBundle ships. Opt-in: if you call this <c>UseRask</c> wires
    ///     <c>UseResponseCompression()</c> ahead of <c>UseStaticFiles</c> automatically; if you
    ///     don't, the host still works but every byte ships uncompressed.
    ///     <para>
    ///         Compression is enabled for HTTPS too (the SDK output isn't user-secret-bearing,
    ///         so CRIME doesn't apply); brotli level is Optimal because the bundle is static and
    ///         the response is held in the middleware's per-response buffer — the CPU cost is
    ///         paid once per (file, encoding) pair and then absorbed by Kestrel's response cache.
    ///     </para>
    /// </summary>
    public static IServiceCollection AddRask(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            // Defaults cover text/* and application/javascript but not application/wasm or
            // application/octet-stream (the DLL/PDB fallback). Without these the largest
            // payloads in the bundle (System.Private.CoreLib.wasm, dotnet.native.wasm) ship
            // uncompressed and the win evaporates.
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
            {
                "application/wasm", "application/octet-stream"
            });
        });

        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);

        return services;
    }
}
