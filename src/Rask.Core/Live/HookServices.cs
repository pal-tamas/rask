using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     The services of a lifecycle hook's part of its event, handed to a hook that asked for the provider itself
///     (<c>Current.Service&lt;IServiceProvider&gt;()</c>): what is resolved through it is that part's, on whichever
///     thread it is asked, until the hook is done. Work the hook detached and did not await is refused past that point
///     — the scope is the hook's, as a request's is in Laravel — rather than handed another event's.
/// </summary>
internal sealed class HookServices(EventServices services, EventLife life) : IKeyedServiceProvider
{
    public object? GetService(Type serviceType) =>
        serviceType == typeof(IServiceProvider) ? this : services.GetService(life, serviceType);

    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        services.GetKeyedService(serviceType, serviceKey);

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        services.GetRequiredKeyedService(serviceType, serviceKey);
}
