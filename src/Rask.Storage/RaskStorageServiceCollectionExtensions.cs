using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Batteries;
using Rask.Hosting.Shared;
using Rask.Storage.Backends;
using Rask.Storage.Serving;

namespace Rask.Storage;

/// <summary>Registers file storage into an <see cref="IServiceCollection"/>.</summary>
public static class RaskStorageServiceCollectionExtensions
{
    internal const string HttpClientName = "Rask.Storage";

    /// <summary>
    /// Registers <see cref="IFiles"/>, the store it writes to, and the background sweep that removes orphaned
    /// bytes. Map the table with <c>modelBuilder.AddRaskStorage()</c> in <c>OnModelCreating</c>, register your
    /// context as an <see cref="IDbContextFactory{TContext}"/>, and serve links with <c>app.MapRaskStorage()</c>.
    /// Idempotent: the first registration wins.
    /// </summary>
    /// <remarks>
    /// Options are read from configuration under <c>Rask:Storage</c> first (<c>Rask__Storage__Provider</c>, …), then
    /// <paramref name="configure"/>, so code wins. They are validated when the app starts — a bad value stops the boot,
    /// naming <c>Rask:Storage</c>, not the first upload.
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the stored-file table.</typeparam>
    public static IServiceCollection AddRaskStorage<TContext>(this IServiceCollection services,
        Action<StorageOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(Clock.TimeProvider); // Rask's clock, so Clock.Fake moves this battery's time too

        // Idempotent, and what makes a hand-wired host (no AddRask) able to sign temporary URLs. Rask's own
        // hosts persist the key ring to the deploy volume, so a link survives a redeploy.
        services.AddDataProtection();

        // The S3 and Azure stores' client. Redirects are never followed — a signed request has no business
        // being replayed somewhere else — and the logging handlers are removed, because a SAS token or a
        // presigned query string in a request URL is a credential.
        services.AddHttpClient(HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            })
            // No client-wide timeout: one PUT may carry up to 5 GiB over a slow link, and a fixed ceiling would fail
            // every large upload identically. Each call runs under the caller's cancellation token instead.
            .ConfigureHttpClient(static client => client.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();

        // Defaults, then Rask:Storage, then the callback; the disk root is settled after all three, and everything is
        // validated at start. Only on the first call: a repeat registers nothing, as AddRaskStorage always has.
        if (services.AddRaskOptions<StorageOptions>(
                StorageConfiguration.SectionName,
                static (section, o) => StorageConfiguration.Bind(section, o),
                configure,
                validate: null))
        {
            services.AddSingleton<IPostConfigureOptions<StorageOptions>, ResolveStorageDiskRoot>();
            services.AddSingleton<IValidateOptions<StorageOptions>, ValidateStorageOptions>();
        }

        services.TryAddSingleton(sp => CreateBackend(sp, sp.GetRequiredService<StorageOptions>()));
        services.TryAddSingleton(sp => new TemporaryUrlProtector(sp.GetRequiredService<IDataProtectionProvider>()));
        services.TryAddSingleton<StorageRuntime>();
        services.TryAddSingleton<IFiles, FileStore<TContext>>();

        // In order: the model check names the missing mapping, the startup check validates configuration, and
        // only then does the sweep start. AddHostedService uses TryAddEnumerable, so a repeat call adds none.
        services.AddHostedService<StorageModelCheck<TContext>>();
        services.AddHostedService<StorageStartupCheck>();
        // The poll is Rask's bookkeeping, not the application's query log, so it runs on a context
        // whose SQL logs at Debug — see HousekeepingContextFactory. Deduplicates on repeat, as
        // AddHostedService does.
        services.AddHousekeepingService<OrphanSweeper<TContext>, TContext>();
        return services;
    }

    // After configuration and the callback, because either may name the root — or leave it to the volume or the content
    // root, which only the host knows.
    private sealed class ResolveStorageDiskRoot(IServiceProvider services) : IPostConfigureOptions<StorageOptions>
    {
        public void PostConfigure(string? name, StorageOptions options)
        {
            if (options.Provider == StorageProvider.Disk)
            {
                var contentRoot = services.GetService<IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory;
                StorageConfiguration.ResolveDiskRoot(options, contentRoot);
            }
        }
    }

    // Not the helper's validate callback: the disk-root check needs the web root, which only the host knows.
    private sealed class ValidateStorageOptions(IServiceProvider services) : IValidateOptions<StorageOptions>
    {
        public ValidateOptionsResult Validate(string? name, StorageOptions options)
        {
            if (name is not null && name != Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            try
            {
                options.Validate(services.GetService<IWebHostEnvironment>()?.WebRootPath);
                return ValidateOptionsResult.Success;
            }
            catch (InvalidOperationException ex)
            {
                return ValidateOptionsResult.Fail($"{StorageConfiguration.SectionName}: {ex.Message}");
            }
        }
    }

    private static IBlobBackend CreateBackend(IServiceProvider services, StorageOptions options) => options.Provider switch
    {
        StorageProvider.Disk => new DiskBlobBackend(options.Disk.Root!),
        StorageProvider.S3 => new S3BlobBackend(Client(services), options.S3, services.GetRequiredService<TimeProvider>()),
        StorageProvider.Azure => new AzureBlobBackend(Client(services), AzureAccount.Parse(options.Azure.ConnectionString),
            options.Azure.Container, services.GetRequiredService<TimeProvider>()),
        _ => throw new InvalidOperationException($"Storage provider {options.Provider} is not supported."),
    };

    private static HttpClient Client(IServiceProvider services) =>
        services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
}
