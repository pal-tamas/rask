using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

// Points every [OffThread] query and command this app handles at the worker, on the page.
//
// The generated contract already has the invoker that sends a message through IRemoteDispatch, closed over its result
// type — the only way to hand back a Task<TResult> without MakeGenericType. It resolves the transport from the provider
// it is given, so it is given one that answers IRemoteDispatch with the off-thread dispatch and everything else as the
// page's own. A browser app that also talks to a server keeps that server's transport for its other messages.
//
// Installed into the process-wide registry and read from the container at each dispatch, so a container without an
// off-thread dispatch — a test, a prerender pass — runs the handler where it is, as it did before.
internal static class OffThreadInvokers
{
    private static readonly Lock Gate = new();
    private static bool _installed;

    internal static void Install()
    {
        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            foreach (var contract in RemoteContractRegistry.All)
            {
                if (Handled(contract) is { } remote)
                {
                    var local = CqrsRegistry.GetRequestInvoker(contract.MessageType);
                    CqrsRegistry.RegisterRequest(contract.MessageType, (provider, message, cancellationToken) =>
                        provider.GetService<OffThreadDispatch>() is { } dispatch
                            ? remote(new Through(provider, dispatch), message, cancellationToken)
                            : local(provider, message, cancellationToken));
                }
            }

            _installed = true;
        }
    }

    // The worker runs a message with a handler here; one handled elsewhere is a remote transport's to carry.
    internal static CqrsRegistry.RequestInvoker? Handled(RemoteContract contract) =>
        contract is { Invoker: { } invoker, LocalInvoker: not null, Kind: not (RemoteMessageKind.Event or RemoteMessageKind.Subscription) }
        && OffThread.IsMarked(contract.MessageType)
            ? invoker
            : null;

    private sealed class Through(IServiceProvider page, IRemoteDispatch dispatch) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IRemoteDispatch) ? dispatch : page.GetService(serviceType);
    }
}
