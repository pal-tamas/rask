using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.ObjectStore;

/// <summary>Registers an <see cref="IObjectStore" />.</summary>
public static class ObjectStoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers an <see cref="IObjectStore" /> over S3 or any S3-compatible store — R2, GCS through
    ///     its interop keys, MinIO, B2, Spaces.
    /// </summary>
    /// <remarks>
    ///     Credentials come from <see cref="ObjectStoreOptions" /> unless an
    ///     <see cref="IObjectStoreCredentials" /> is already registered — register
    ///     <see cref="InMemoryObjectStoreCredentials" /> first for a credential supplied at runtime rather
    ///     than from configuration, which is the browser case.
    /// </remarks>
    public static IServiceCollection AddRaskS3ObjectStore(
        this IServiceCollection services, Action<ObjectStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IObjectStoreCredentials, OptionsObjectStoreCredentials>();
        services.AddHttpClient<IObjectStore, S3ObjectStore>();
        return services;
    }

    /// <summary>Registers an <see cref="IObjectStore" /> over Azure Blob Storage, authenticated by a SAS token.</summary>
    /// <inheritdoc cref="AddRaskS3ObjectStore" path="/remarks" />
    public static IServiceCollection AddRaskAzureBlobObjectStore(
        this IServiceCollection services, Action<ObjectStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);
        services.TryAddSingleton<IObjectStoreCredentials, OptionsObjectStoreCredentials>();
        services.AddHttpClient<IObjectStore, AzureBlobObjectStore>();
        return services;
    }

    /// <summary>
    ///     Registers <see cref="InMemoryObjectStoreCredentials" /> as the credential source, for a
    ///     credential the user supplies at runtime. Call before
    ///     <see cref="AddRaskS3ObjectStore" />/<see cref="AddRaskAzureBlobObjectStore" />, which only fall
    ///     back to configuration when nothing else is registered.
    /// </summary>
    /// <remarks>
    ///     Resolve the concrete <see cref="InMemoryObjectStoreCredentials" /> to call
    ///     <see cref="InMemoryObjectStoreCredentials.Set" /> once the credential is known.
    /// </remarks>
    public static IServiceCollection AddRaskInMemoryObjectStoreCredentials(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryObjectStoreCredentials>();
        services.TryAddSingleton<IObjectStoreCredentials>(
            static sp => sp.GetRequiredService<InMemoryObjectStoreCredentials>());
        return services;
    }
}
