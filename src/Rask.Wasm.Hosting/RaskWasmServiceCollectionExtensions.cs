using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Hosting.Shared;

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
    /// <remarks>
    ///     In an app that references <b>both</b> hosts — a wasm-hosted app that also mounts the
    ///     server-rendered operator dashboard — call <see cref="AddRaskWasmHost" /> instead. Both
    ///     packages define an <c>AddRask(this IServiceCollection)</c>, and with both namespaces imported
    ///     C# does not report an ambiguity: this overload takes no optional parameters and
    ///     <c>Rask.Server</c>'s takes two, so the "fewer defaulted arguments" tie-break silently
    ///     selects this one. The app then compiles, starts without the live runtime registered, and
    ///     fails on the first request with a missing-service error naming a type the author never used.
    /// </remarks>
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

        // The same host defaults Rask.Server's AddRask applies, because this is a web host too and the
        // failures do not care which package started it: an ephemeral key ring signs every user of a
        // cookie-authenticated bundle host out on each deploy, and hosted services stopped one at a time
        // sum past the SIGKILL. Source-linked from Rask.Hosting.Shared; TryAddEnumerable so an app that
        // calls both this and AddRaskServer (the dashboard case) registers one of each.
        //
        // AddDataProtection FIRST, and unconditionally: it registers ASP.NET's own
        // DataProtectionOptionsSetup, which overwrites ApplicationDiscriminator without checking. The
        // scaffolded wasm-hosted app calls AddAuthentication below this line, which would otherwise pull
        // Data Protection in afterwards and quietly revert the discriminator to the content-root default.
        services.AddDataProtection();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<KeyManagementOptions>, RaskDataProtectionSetup>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<DataProtectionOptions>, RaskDataProtectionSetup>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HostOptions>, RaskShutdownDefaults>());

        return services;
    }

    /// <summary>
    ///     <see cref="AddRask" /> under a name only this package defines — for an app that references
    ///     both hosts and therefore cannot say <c>AddRask()</c> and mean it.
    ///     <para>
    ///         Identical behaviour; the point is the name. A wasm-hosted app that mounts the operator
    ///         dashboard registers <c>Rask.Server</c>'s runtime with <c>AddRask(…)</c> and this host's
    ///         compression with <c>AddRaskWasmHost()</c>, and each call says which host it means
    ///         instead of depending on an overload-resolution tie-break to guess right.
    ///     </para>
    /// </summary>
    public static IServiceCollection AddRaskWasmHost(this IServiceCollection services) =>
        services.AddRask();
}
