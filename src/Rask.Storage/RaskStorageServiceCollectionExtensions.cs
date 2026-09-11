using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
    /// Options are read from configuration under <c>Storage</c> first, then <paramref name="configure"/>, so code
    /// wins. They are validated when the app starts — a bad value stops the boot, not the first upload.
    /// </remarks>
    /// <typeparam name="TContext">The application <see cref="DbContext"/> that owns the stored-file table.</typeparam>
    public static IServiceCollection AddRaskStorage<TContext>(this IServiceCollection services,
        Action<StorageOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

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
            .ConfigureHttpClient(static client => client.Timeout = TimeSpan.FromMinutes(10))
            .RemoveAllLoggers();

        services.TryAddSingleton(sp => BuildOptions(sp, configure));
        services.TryAddSingleton(sp => CreateBackend(sp, sp.GetRequiredService<StorageOptions>()));
        services.TryAddSingleton(sp => new TemporaryUrlProtector(sp.GetRequiredService<IDataProtectionProvider>()));
        services.TryAddSingleton<StorageRuntime>();
        services.TryAddSingleton<IFiles, Files<TContext>>();

        // In order: the model check names the missing mapping, the startup check validates configuration, and
        // only then does the sweep start. AddHostedService uses TryAddEnumerable, so a repeat call adds none.
        services.AddHostedService<StorageModelCheck<TContext>>();
        services.AddHostedService<StorageStartupCheck>();
        services.AddHostedService<OrphanSweeper<TContext>>();
        return services;
    }

    private static StorageOptions BuildOptions(IServiceProvider services, Action<StorageOptions>? configure)
    {
        var options = new StorageOptions();
        StorageConfiguration.Apply(options, services.GetService<IConfiguration>());
        configure?.Invoke(options);

        var contentRoot = services.GetService<IHostEnvironment>()?.ContentRootPath ?? AppContext.BaseDirectory;
        if (options.Provider == StorageProvider.Disk)
        {
            StorageConfiguration.ResolveDiskRoot(options, contentRoot);
        }

        options.Validate(services.GetService<IWebHostEnvironment>()?.WebRootPath);
        return options;
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
