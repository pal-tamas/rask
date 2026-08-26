using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Cqrs.Client;

/// <summary>Registers remote dispatch into a client app.</summary>
public static class RaskCqrsClientServiceCollectionExtensions
{
    /// <summary>
    ///     Makes this process a Rask.Cqrs <b>client</b>: every message it dispatches is sent to the
    ///     server. Call it once at startup — it is the only Rask.Cqrs line a client project needs.
    /// </summary>
    /// <param name="services">The app's service collection.</param>
    /// <param name="configure">Optional transport configuration — the server's address, credentials, limits.</param>
    /// <remarks>
    ///     <para>
    ///         <c>AddRaskCqrs()</c> is called for you, so a client never registers the mediator itself.
    ///     </para>
    ///     <para>
    ///         <b>A client is a pure client.</b> Every contract gets a remote invoker, whether or not this
    ///         process happens to contain a handler for it — there is no conditional to reason about at a
    ///         call site, and no way for a stray client-side handler to quietly intercept a message meant
    ///         for the server. Notifications are the one exception, and deliberately so: they fan out
    ///         rather than being handled once, so a client's own handlers still run and the notification
    ///         *also* travels to the server.
    ///     </para>
    ///     <para>
    ///         Installing the invokers mutates the process-wide <see cref="CqrsRegistry" />, because that
    ///         is where dispatch looks them up. Calling this twice is a no-op rather than a double
    ///         registration.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddRaskCqrsClient(
        this IServiceCollection services,
        Action<RaskCqrsClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(static d => d.ServiceType == typeof(ClientMarker)))
        {
            return services;
        }

        services.AddSingleton(new ClientMarker());

        var options = new RaskCqrsClientOptions();
        configure?.Invoke(options);
        options.Validate();

        // The one line: a client project references Rask.Cqrs.Client and calls this, nothing else.
        services.AddRaskCqrs();

        services.TryAddSingleton(options);
        services.TryAddSingleton<IRemoteDispatch>(sp => new RemoteDispatch(ResolveHttpClient(sp, options), options));

        InstallRemoteInvokers();

        return services;
    }

    // Installed once per process, not once per ServiceCollection. The marker above already stops a
    // repeated registration on the SAME collection, but the registry these invokers go into is static and
    // process-wide, so a second collection — a test, a rebuilt container, a host composing two — would
    // reach here again. For a request that is merely wasteful: the invoker is replaced by an identical
    // one. For a notification it is a correctness bug, because the composed invoker captures whatever
    // was registered before it and would wrap ITSELF, turning one publish into two sends, then three.
    private static readonly Lock InstallGate = new();
    private static bool _invokersInstalled;

    // Every request contract becomes remote; every notification composes with whatever handles it here.
    //
    // Registered through CqrsRegistry's manual path, which the registry applies last when it rebuilds —
    // so a remote invoker deterministically wins over the generated local one rather than depending on
    // module-initializer order.
    private static void InstallRemoteInvokers()
    {
        lock (InstallGate)
        {
            if (_invokersInstalled)
            {
                return;
            }

            _invokersInstalled = true;
        }

        foreach (var contract in RemoteContractRegistry.All)
        {
            if (contract.Kind == RemoteMessageKind.Notification)
            {
                InstallNotification(contract);
                continue;
            }

            if (contract.Invoker is { } invoker)
            {
                CqrsRegistry.RegisterRequest(contract.MessageType, invoker);
            }
        }
    }

    private static void InstallNotification(RemoteContract contract)
    {
        // Captured before the replacement is installed, so the composed invoker still runs whatever the
        // generated one did. Publishing on a client should reach this process's own reactors — a badge, a
        // toast — and also travel; replacing the invoker outright would silently drop the local ones.
        var local = CqrsRegistry.FindNotificationInvoker(contract.MessageType);

        CqrsRegistry.RegisterNotification(contract.MessageType, async (provider, notification, cancellationToken) =>
        {
            if (local is not null)
            {
                await local(provider, notification, cancellationToken).ConfigureAwait(false);
            }

            var transport = provider.GetService<IRemoteDispatch>()
                            ?? throw new InvalidOperationException(
                                "No remote transport is registered, so this notification cannot reach the server. "
                                + "Call AddRaskCqrsClient() during startup.");

            await transport.PublishAsync(contract, notification, cancellationToken).ConfigureAwait(false);
        });
    }

    // An absolute BaseAddress means a client talking to a server on another origin. Without one, the
    // app's own origin is meant, which is what a browser client wants: the request is
    // same-origin, so the session cookie rides it and no CORS preflight is involved. The container's own
    // HttpClient carries that origin, and every Rask WASM template registers one.
    private static HttpClient ResolveHttpClient(IServiceProvider provider, RaskCqrsClientOptions options)
    {
        if (options.BaseAddress is not null)
        {
            return new HttpClient { BaseAddress = options.BaseAddress, Timeout = options.Timeout };
        }

        var existing = provider.GetService<HttpClient>();
        if (existing?.BaseAddress is not null)
        {
            return existing;
        }

        throw new InvalidOperationException(
            "Rask.Cqrs.Client does not know where to send messages. Either set BaseAddress in "
            + "AddRaskCqrsClient(o => o.BaseAddress = …) — which is what a client on another origin does — "
            + "or register an HttpClient whose BaseAddress is the app's own origin, which is what a "
            + "same-origin client wants so its session cookie rides every request.");
    }

    // Sentinel marking that AddRaskCqrsClient already ran on this collection.
    private sealed class ClientMarker;
}
