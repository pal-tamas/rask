using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Rask.DevTools.Server;

/// <summary>Binds Kestrel to the <c>.test</c> name, when <see cref="EditorDevHost.Resolve" /> finds one.</summary>
/// <remarks>
///     An explicit listen replaces the launch profile's <c>applicationUrl</c> — Kestrel logs that it is
///     overriding the configured addresses — which is the point: the app answers on the name the certificate
///     is for, HTTPS only, on loopback, exactly as it does under <c>rask dev</c>.
/// </remarks>
internal sealed class EditorDevHostKestrelSetup(IHostEnvironment environment, IDevHostMachine machine)
    : IConfigureOptions<KestrelServerOptions>
{
    public void Configure(KestrelServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (Resolve(environment, machine) is not { } host)
        {
            return;
        }

        options.Listen(IPAddress.Loopback, host.Port, listen => listen.UseHttps(LoadCertificate(host)));
    }

    internal static EditorDevHost? Resolve(IHostEnvironment environment, IDevHostMachine machine) =>
        EditorDevHost.Resolve(
            Assembly.GetEntryAssembly(),
            environment.IsDevelopment(),
            environment.ApplicationName,
            environment.ContentRootPath,
            Environment.GetEnvironmentVariable,
            machine);

    /// <summary>
    ///     The PEM pair <c>rask dev</c> issued. On Windows, SChannel cannot use the ephemeral key a PEM import
    ///     produces, so the pair goes through PKCS#12 once — the standard workaround, and what Kestrel's own
    ///     certificate loader does for the same files.
    /// </summary>
    internal static X509Certificate2 LoadCertificate(EditorDevHost host)
    {
        var pem = X509Certificate2.CreateFromPemFile(host.CertificatePath, host.KeyPath);
        if (!OperatingSystem.IsWindows())
        {
            return pem;
        }

        using (pem)
        {
            return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), password: null);
        }
    }
}
