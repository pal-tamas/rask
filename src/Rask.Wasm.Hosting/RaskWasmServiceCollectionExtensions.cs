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
    ///     <para>
    ///         Named for the host it sets up rather than <c>AddRask</c>, and that is load-bearing.
    ///         <c>Rask.Server</c> also defines an <c>AddRask(this IServiceCollection)</c> — with every
    ///         parameter optional — so in an app that references both packages a bare <c>AddRask()</c>
    ///         was NOT reported as ambiguous: C# prefers the candidate with no omitted optional
    ///         parameters, so this one won the tie-break silently. The app compiled, started without the
    ///         live runtime registered, and died at <c>UseRask&lt;TApp&gt;()</c> with a missing-service
    ///         error naming a type the author had never heard of.
    ///     </para>
    ///     <para>
    ///         The two names used to coexist, with this hazard written down beside them and an alias to
    ///         reach for instead. That held exactly as long as every call site remembered — until #1095,
    ///         when a configuration refactor dropped the named argument that had been disambiguating the
    ///         scaffolded <c>--wasm</c> app by accident, and every one of those apps stopped starting.
    ///         One name per behaviour is what actually removes the failure.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddRaskWasmHost(this IServiceCollection services)
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
            ServiceDescriptor.Singleton<IConfigureOptions<KeyManagementOptions>, RaskDataProtectionSetup>(
                RaskDataProtectionSetup.Create));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<DataProtectionOptions>, RaskDataProtectionSetup>(
                RaskDataProtectionSetup.Create));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<HostOptions>, RaskShutdownDefaults>());

        return services;
    }

}
