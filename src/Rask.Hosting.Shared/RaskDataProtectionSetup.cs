using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Rask.Hosting.Shared;

/// <summary>
/// Persists the Data Protection key ring onto durable storage when the app is running somewhere that has
/// some — which, for a container, is the only way anything it protects survives a redeploy.
/// </summary>
/// <remarks>
/// <para>
/// The default key ring is written inside the container, and every deploy replaces the container. Without
/// this, a redeploy mints a fresh ring and everything sealed under the old one stops opening: every auth
/// cookie already issued is silently rejected, so all your signed-in users are signed out, and every Rask
/// session-resume token becomes unreadable, so reconnecting clients fall back to a full reload. Nothing
/// logs an error, because from the app's side these are simply payloads it cannot unprotect.
/// </para>
/// <para>
/// <b>It applies only where there is somewhere durable to write.</b> The location is
/// <c>Rask:DataProtection:KeyPath</c> when set, otherwise <c>/data/keys</c> when <c>/data</c> exists —
/// which is the volume <c>rask deploy</c> mounts, and the same place the database lives. On a plain
/// <c>dotnet run</c> neither is present, nothing happens, and ASP.NET's per-user development key ring
/// applies exactly as before. Set the key to an empty value to opt out where <c>/data</c> does exist.
/// </para>
/// <para>
/// <see cref="DataProtectionOptions.ApplicationDiscriminator"/> matters as much as the path. Its default is
/// derived from the content root, which differs between the build image and the runtime image — so two
/// containers sharing one key ring would still derive different keys from it. Pinning it to the application
/// name makes the shared ring actually shared.
/// </para>
/// <para>
/// Registered after <c>AddDataProtection()</c>, so it overrides ASP.NET's discovered default while an app
/// that configures its own ring <em>after</em> <c>AddRask</c> still wins — options setups run in
/// registration order, and the last one to write the value decides.
/// </para>
/// </remarks>
internal sealed class RaskDataProtectionSetup(
    IConfiguration? configuration,
    IHostEnvironment? environment,
    ILoggerFactory? loggerFactory)
    : IConfigureOptions<KeyManagementOptions>, IConfigureOptions<DataProtectionOptions>
{
    // The volume rask deploy mounts. Kept as a field so the probe is named once rather than spelled twice.
    private const string DeployVolume = "/data";

    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    /// <summary>
    /// Builds the setup from whatever host services the container actually has, rather than demanding them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every host builder registers <see cref="IConfiguration"/> and <see cref="IHostEnvironment"/>, so in a
    /// real app this resolves all three and nothing about the behaviour above changes. It exists for the
    /// container that is NOT a host: a test fixture or a benchmark harness composing <c>AddRask()</c> into a
    /// bare <c>ServiceCollection</c>. Activating this type by constructor there threw
    /// <c>InvalidOperationException: Unable to resolve service for type 'IConfiguration'</c> — and not at
    /// registration, but lazily, the first time anything materialised the Data Protection options, which is
    /// a long way from the cause (#922).
    /// </para>
    /// <para>
    /// That mattered beyond the harness: <c>AddRask</c> deliberately ASKS for the Data Protection provider
    /// with <c>GetService</c> rather than assuming it, so a host without one degrades instead of failing.
    /// A hard constructor dependency here defeated that — the provider was registered, so the ask succeeded,
    /// and then building it threw. Missing services now mean the framework default key ring, which is the
    /// same thing a plain <c>dotnet run</c> already gets.
    /// </para>
    /// </remarks>
    public static RaskDataProtectionSetup Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new RaskDataProtectionSetup(
            services.GetService<IConfiguration>(),
            services.GetService<IHostEnvironment>(),
            services.GetService<ILoggerFactory>());
    }

    /// <summary>
    /// The directory the key ring belongs in, or <c>null</c> when this host has nowhere durable to put it
    /// and should keep the framework default.
    /// </summary>
    internal string? ResolveKeyPath()
    {
        // A container with no IConfiguration reads as "nothing configured", which lands on the /data probe
        // below — the same answer a host whose configuration omits the key gives.
        var configured = configuration?["Rask:DataProtection:KeyPath"];
        if (configured is not null)
        {
            // An explicitly empty value is an opt-out, not a request to write to the working directory.
            return string.IsNullOrWhiteSpace(configured) ? null : configured;
        }

        return Directory.Exists(DeployVolume) ? Path.Combine(DeployVolume, "keys") : null;
    }

    /// <inheritdoc/>
    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (ResolveKeyPath() is not { } keyPath)
        {
            return;
        }

        // Creating the directory can fail for reasons that are nothing to do with this app: /data is a
        // conventional mount point, so it can exist and be root-owned while the container runs as a
        // non-root user. This runs from an options setup, which is resolved lazily — so an unhandled
        // throw here would surface as a 500 on the first cookie or antiforgery operation, a long way from
        // the cause. Fall back to the framework's own ring and say so instead: the app keeps working, and
        // the warning names the one thing to fix.
        try
        {
            options.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(keyPath), _loggerFactory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _loggerFactory.CreateLogger<RaskDataProtectionSetup>().LogWarning(
                ex,
                "Rask could not persist the Data Protection key ring to {KeyPath}, so the framework default "
                + "is being used instead. That ring does not outlive this process: every deploy will mint a "
                + "new one, signing out every user and invalidating every session-resume record. Grant the "
                + "app write access to that directory, point Rask:DataProtection:KeyPath somewhere writable, "
                + "or set it to an empty value to manage the ring yourself.",
                keyPath);
        }
    }

    /// <inheritdoc/>
    public void Configure(DataProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (ResolveKeyPath() is null)
        {
            // Leave the content-root-derived default alone: without a shared ring there is nothing for a
            // stable discriminator to unlock, and changing it would invalidate a dev machine's existing keys.
            return;
        }

        if (environment is null)
        {
            // There IS a shared ring here but no application name to pin it to, so the discriminator keeps
            // its content-root-derived default and two containers sharing the ring still derive different
            // keys from it.
            //
            // Best-effort, and honestly so: this is only reachable in a container that is not a host, and
            // such a container usually has no ILoggerFactory either (AddRask does not call AddLogging), in
            // which case the warning goes to NullLoggerFactory and nobody reads it. It is worth emitting
            // for the container that DID call AddLogging; it is not a substitute for the fact that a host
            // with a shared key ring should register an IHostEnvironment.
            _loggerFactory.CreateLogger<RaskDataProtectionSetup>().LogWarning(
                "Rask is persisting the Data Protection key ring, but this container has no IHostEnvironment, "
                + "so the application discriminator keeps its content-root default. Two hosts sharing the ring "
                + "will still derive different keys from it. Register an IHostEnvironment, or set "
                + "DataProtectionOptions.ApplicationDiscriminator yourself.");
            return;
        }

        options.ApplicationDiscriminator = environment.ApplicationName;
    }
}
