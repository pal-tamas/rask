using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     The services of one event, handed to a lifecycle hook that asked for the provider itself
///     (<c>Current.Service&lt;IServiceProvider&gt;()</c>): what is resolved through it is that event's, wherever and
///     whenever it is asked — a task the hook detached included — while the hold it carries keeps the scope.
/// </summary>
internal sealed class EventServicesLease(EventServices services, EventLife life) : IKeyedServiceProvider
{
    public object? GetService(Type serviceType) =>
        serviceType == typeof(IServiceProvider) ? this : services.GetService(life, serviceType);

    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        services.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        services.GetRequiredKeyedService(serviceType, serviceKey);
}
