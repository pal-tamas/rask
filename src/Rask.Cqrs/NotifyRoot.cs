namespace Rask.Cqrs;

/// <summary>
///     Hands <see cref="Notify" /> the provider it opens its scopes from.
/// </summary>
/// <remarks>
///     Registered as a singleton and resolved by the first <see cref="Dispatcher" /> the container builds, because
///     the <see cref="IServiceProvider" /> injected into a singleton is the container's root — whatever the
///     container is. A host would have been served by an <c>IHostedService</c>; a browser app and a test's bare
///     <c>ServiceCollection</c> start no hosted services, and those are exactly the places <c>Notify.Send</c> is
///     reached for in a test.
/// </remarks>
internal sealed class NotifyRoot
{
    public NotifyRoot(IServiceProvider services) => Notify.Configure(services);
}
