using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Hosting.Shared;

namespace Rask.DevTools.Server;

/// <summary>
///     What an app VS Code's F5 launched needs from Rask that <c>rask dev</c> would otherwise have provided:
///     the <c>.test</c> address when it is already set up, and a line telling the editor where to point the
///     browser.
/// </summary>
internal static class EditorDevSessionServices
{
    /// <summary>
    ///     Registers both, but only for a build that was a dev session — an ordinary Debug build carries
    ///     nothing of this. Whether they then act is decided at startup, when the environment is known.
    /// </summary>
    internal static void Add(IServiceCollection services) => Add(services, Assembly.GetEntryAssembly());

    /// <summary>The same, for an explicit app assembly — the seam the tests use.</summary>
    internal static void Add(IServiceCollection services, Assembly? entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!EditorDevSession.IsDevSessionBuild(entryAssembly))
        {
            return;
        }

        services.TryAddSingleton<IDevHostMachine, DevHostMachine>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, EditorDevHostKestrelSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EditorDevSessionAnnouncer>());
    }
}

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

/// <summary>
///     Writes <c>Rask dev: open &lt;url&gt;</c> once the app is listening, which the scaffolded
///     <c>launch.json</c>'s <c>serverReadyAction</c> opens.
/// </summary>
/// <remarks>
///     Written to standard output rather than logged: the editor watches the debug console for the line, and
///     an app that filters or reformats its logs must not quietly stop the browser from opening.
/// </remarks>
internal sealed class EditorDevSessionAnnouncer(
    IHostEnvironment environment,
    IDevHostMachine machine,
    IHostApplicationLifetime lifetime,
    IServer server) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!EditorDevSession.IsActive(Assembly.GetEntryAssembly(), environment.IsDevelopment(), Environment.GetEnvironmentVariable))
        {
            return Task.CompletedTask;
        }

        lifetime.ApplicationStarted.Register(() =>
        {
            var url = EditorDevHostKestrelSetup.Resolve(environment, machine)?.Url
                      ?? PreferredAddress(server.Features.Get<IServerAddressesFeature>()?.Addresses);

            if (url is not null)
            {
                Console.Out.WriteLine(EditorDevSession.OpenLinePrefix + url);
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     The address a browser should open: HTTPS when the app listens on it, with a wildcard bind turned
    ///     into <c>localhost</c> (a browser cannot open <c>0.0.0.0</c> or <c>[::]</c>).
    /// </summary>
    internal static string? PreferredAddress(IEnumerable<string>? addresses)
    {
        var list = addresses?.ToList();
        if (list is not { Count: > 0 })
        {
            return null;
        }

        var chosen = list.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ?? list[0];

        return chosen
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
            .Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://*", "://localhost", StringComparison.Ordinal);
    }
}
