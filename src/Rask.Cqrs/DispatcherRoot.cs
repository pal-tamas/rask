namespace Rask.Cqrs;

/// <summary>
///     Hands <see cref="Dispatcher" /> the provider it opens its scopes from when called outside any work.
/// </summary>
/// <remarks>
///     Registered as a singleton and resolved by the first <see cref="IDispatcher" /> the container builds, because
///     the <see cref="IServiceProvider" /> injected into a singleton is the container's root — whatever the
///     container is. A host is served by <see cref="DispatcherStart" /> too; a browser app and a test's bare
///     <c>ServiceCollection</c> start no hosted services, and are served by this alone.
/// </remarks>
internal sealed class DispatcherRoot
{
    public DispatcherRoot(IServiceProvider services) => Dispatcher.Start(services);
}
