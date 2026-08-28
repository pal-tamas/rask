using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rask.Server;

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
    IConfiguration configuration,
    IHostEnvironment environment,
    ILoggerFactory loggerFactory)
    : IConfigureOptions<KeyManagementOptions>, IConfigureOptions<DataProtectionOptions>
{
    // The volume rask deploy mounts. Kept as a field so the probe is named once rather than spelled twice.
    private const string DeployVolume = "/data";

    /// <summary>
    /// The directory the key ring belongs in, or <c>null</c> when this host has nowhere durable to put it
    /// and should keep the framework default.
    /// </summary>
    internal string? ResolveKeyPath()
    {
        var configured = configuration["Rask:DataProtection:KeyPath"];
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

        options.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(keyPath), loggerFactory);
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

        options.ApplicationDiscriminator = environment.ApplicationName;
    }
}
