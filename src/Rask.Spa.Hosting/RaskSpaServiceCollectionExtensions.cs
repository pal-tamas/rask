using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Spa.Hosting;

/// <summary>
///     Registers what <see cref="RaskSpaEndpointExtensions.UseRaskSpa" /> uses when it is there.
/// </summary>
public static class RaskSpaServiceCollectionExtensions
{
    /// <summary>
    ///     Adds brotli and gzip response compression covering the types a bundler emits.
    /// </summary>
    /// <remarks>
    ///     Optional: <c>UseRaskSpa</c> works without it, just uncompressed for any file with no
    ///     precompressed sibling on disk. Named <c>AddRaskSpaHost</c> rather than <c>AddRask</c>
    ///     deliberately — <c>Rask.Server</c> and <c>Rask.Wasm.Hosting</c> both declare an
    ///     <c>AddRask(this IServiceCollection)</c>, and in an app referencing two of them a bare call is
    ///     resolved silently by the fewest-defaulted-arguments tie-break rather than reported as
    ///     ambiguous. Every host here names the host it means.
    /// </remarks>
    /// <param name="services">The app's service collection.</param>
    /// <returns><paramref name="services" />, for chaining.</returns>
    public static IServiceCollection AddRaskSpaHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddResponseCompression(options =>
        {
            // On by default here. The bodies are static assets already on their way through a TLS
            // connection, and the BREACH concern that makes this off-by-default applies to responses
            // carrying a secret alongside attacker-controlled input — which a bundle chunk is not.
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();

            // The framework's default list predates these three. text/javascript is the one that
            // matters: it is what a modern bundler serves ES modules as, and leaving it out means the
            // largest file in the app ships uncompressed while application/javascript, the type
            // nothing emits any more, is covered.
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
            [
                "text/javascript",
                "image/svg+xml",
                "application/manifest+json",
            ]);
        });

        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);

        return services;
    }
}
