using System.Collections.Concurrent;

namespace Rask.Cqrs;

// Which messages carry [OffThread], asked once per type, and the server's half of what it means.
internal static class OffThread
{
    private static readonly ConcurrentDictionary<Type, bool> Marked = new();

    internal static bool IsMarked(Type messageType) =>
        Marked.GetOrAdd(messageType, static type => type.IsDefined(typeof(OffThreadAttribute), inherit: false));

    // A server has a pool to hand the handler to, and handing it there is what keeps a live session answering while
    // a synchronous handler works. A browser has one thread: the page sends the message to its worker instead
    // (Rask.Wasm installs that invoker), and the worker runs it where it already is.
    private static bool Hops(object request) => !OperatingSystem.IsBrowser() && IsMarked(request.GetType());

    internal static Task<TResult> Invoke<TResult>(
        CqrsRegistry.RequestInvoker invoker, IServiceProvider provider, object request, CancellationToken cancellationToken) =>
        Hops(request)
            ? Pooled<TResult>(invoker, provider, request, cancellationToken)
            : (Task<TResult>)invoker(provider, request, cancellationToken);

    internal static Task Invoke(
        CqrsRegistry.RequestInvoker invoker, IServiceProvider provider, object request, CancellationToken cancellationToken) =>
        Hops(request)
            ? Task.Run(() => invoker(provider, request, cancellationToken), cancellationToken)
            : invoker(provider, request, cancellationToken);

    // Their own methods, so a closure is allocated only for a message that hops: captured in the dispatcher, it
    // would be allocated for every dispatch.
    private static Task<TResult> Pooled<TResult>(
        CqrsRegistry.RequestInvoker invoker, IServiceProvider provider, object request, CancellationToken cancellationToken) =>
        Task.Run(() => (Task<TResult>)invoker(provider, request, cancellationToken), cancellationToken);
}
