using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Batteries;

namespace Rask.Storage;

/// <summary>Storage's claim on the application's model, checked once at boot (see <c>BatteryModelCheck</c>).</summary>
internal sealed class StorageModelCheck<TContext>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
{
    protected override string Battery => "Storage";

    protected override Type Entity => typeof(StoredFile);

    protected override string MapCall => "AddRaskStorage";
}

/// <summary>
/// Fails the boot on bad storage configuration, and warns when the file routes were never mapped.
/// </summary>
/// <remarks>
/// Resolving <see cref="StorageRuntime"/> builds and validates the options, so a mistyped
/// <c>Rask__Storage__Provider</c> stops the app starting rather than surfacing on the first upload. The route warning
/// is a warning, not a failure: an app may only ever use <see cref="IFiles.Download"/> behind its own endpoint.
/// </remarks>
internal sealed class StorageStartupCheck(IServiceProvider services, ILogger<StorageStartupCheck> logger) : IHostedService
{
    /// <summary>Where storage settings lived before <c>Rask:Storage</c>.</summary>
    internal const string LegacySectionName = "Storage";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var runtime = services.GetRequiredService<StorageRuntime>();

        // The section moved (#1080) and the old one is simply not read, so an app upgraded without renaming its keys
        // would quietly fall back to the defaults — a disk store where it configured a bucket. Say so at boot.
        if (services.GetService<IConfiguration>()?.GetSection(LegacySectionName).Exists() == true)
        {
            logger.LogWarning(
                "Found a top-level \"Storage\" configuration section, which Rask.Storage no longer reads: its settings "
                + "moved to \"Rask:Storage\" (environment variables Rask__Storage__Provider, Rask__Storage__S3__Bucket, …). "
                + "Until they move, storage runs on {Provider} with the defaults and whatever AddRaskStorage sets in code.",
                runtime.Options.Provider);
        }

        if (!SweepPolicy.MayDelete(runtime.Backend.Provider, runtime.Options.Prefix))
        {
            logger.LogWarning(
                "Rask.Storage writes to {Provider} with no Rask__Storage__Prefix, so the orphan sweep only reports what it would "
                + "remove: a bucket is easily shared, and without a prefix it cannot tell this app's files from another "
                + "environment's. Set Rask__Storage__Prefix (for example \"myapp/\") to let it clean up.",
                runtime.Backend.Provider);
        }

        if (!runtime.EndpointsMapped && services.GetService<IWebHostEnvironment>() is not null)
        {
            logger.LogWarning(
                "Rask.Storage is registered but MapRaskStorage() was never called, so links from files.Url(...) and "
                + "files.TemporaryUrlAsync(...) will answer 404. Add app.MapRaskStorage(); after app.UseRask<App>();.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
