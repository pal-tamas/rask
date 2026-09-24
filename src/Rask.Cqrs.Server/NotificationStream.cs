using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Rask.Cqrs.Server;

/// <summary>
///     Serves a remote subscription: <c>GET {prefix}/events/{name}?m={json}</c>, answered with a
///     <c>text/event-stream</c> of the notification's generated JSON for as long as the client stays connected.
/// </summary>
/// <remarks>
///     <para>
///         <b>Closed unless opened.</b> Two names answer here: an <see cref="ISubscription{TNotification}" /> record,
///         whose <see cref="IWatchPolicy{TSubscription}" /> decides per subscription — it arrives as <c>?m=</c>, the
///         same JSON a query's message travels as — and a notification whose own record declares <c>[Authorize]</c> /
///         <c>[AllowAnonymous]</c>, watched by type. Anything else answers 404, the same as a name that does not exist,
///         so an app's auth and domain events are never one request away.
///     </para>
///     <para>
///         <b>Authenticated by default,</b> exactly like a request: only a record marked <c>[AllowAnonymous]</c> lets a
///         signed-out caller subscribe, and that check comes before the name is judged, so nobody can enumerate the
///         notifications an app has.
///     </para>
///     <para>
///         The first event is <c>ready</c>, written once the policy has admitted the subscription; the client reads it
///         as "live". A comment every <see cref="RaskCqrsServerOptions.EventKeepAlive" /> stops a proxy closing a quiet
///         stream, and tells the server promptly when the client has gone.
///     </para>
/// </remarks>
internal static class NotificationStream
{
    private static readonly byte[] ReadyFrame =
        Encoding.UTF8.GetBytes($"event: {RemoteEndpointDefaults.ReadyEvent}\ndata:\n\n");

    private static readonly byte[] KeepAliveFrame = Encoding.UTF8.GetBytes(": keep-alive\n\n");

    private static readonly byte[] DataPrefix = Encoding.UTF8.GetBytes("data: ");

    private static readonly byte[] FrameEnd = Encoding.UTF8.GetBytes("\n\n");

    public static async Task ServeAsync(HttpContext context, RaskCqrsServerOptions options)
    {
        if (!context.Request.Headers.ContainsKey(RemoteEndpointDefaults.RequestHeader))
        {
            await RaskCqrsEndpointExtensions.ProblemAsync(context, StatusCodes.Status400BadRequest,
                "Not a Rask.Cqrs request", $"The {RemoteEndpointDefaults.RequestHeader} header is required.")
                .ConfigureAwait(false);
            return;
        }

        var name = context.Request.RouteValues["name"] as string;
        RemoteContract? contract = null;
        // A subscription's result codec is what writes each delivered notification, so one without it is not
        // servable — the same 404 as a name nobody registered.
        if (!string.IsNullOrEmpty(name) && RemoteContractRegistry.TryGet(name, out var found)
                                        && found is { CarriesFiles: false }
                                        && (found.Kind == RemoteMessageKind.Notification
                                            || (found.Kind == RemoteMessageKind.Subscription
                                                && found.WriteResult is not null)))
        {
            contract = found;
        }

        // Before the name is judged, as for a request: a 404 for an unknown name and a 401 for a known one would let
        // a signed-out caller map the app's notifications one guess at a time.
        if (options.RequireAuthenticatedUser
            && contract?.SubscribeAnonymously != true
            && context.User.Identity?.IsAuthenticated != true)
        {
            await RaskCqrsEndpointExtensions.ProblemAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized", null)
                .ConfigureAwait(false);
            return;
        }

        // A subscription record is opened by its policy; a bare notification only by what its own record declares.
        if (contract is null
            || (contract.Kind == RemoteMessageKind.Notification && !contract.SubscribeDeclared))
        {
            await RaskCqrsEndpointExtensions.ProblemAsync(context, StatusCodes.Status404NotFound, "Unknown notification", null)
                .ConfigureAwait(false);
            return;
        }

        if (!await RaskCqrsEndpointExtensions.AuthorizedAsync(
                    context, contract.Name, contract.SubscribeAnonymously, contract.SubscribeRoles, contract.SubscribePolicy)
                .ConfigureAwait(false))
        {
            return;
        }

        var encoded = context.Request.Query[RemoteEndpointDefaults.MessageQueryParameter].ToString();
        object? subscription = null;
        if (contract.Kind == RemoteMessageKind.Subscription)
        {
            try
            {
                subscription = string.IsNullOrEmpty(encoded)
                    ? null
                    : NotificationWire.DecodeMessage(contract, Encoding.UTF8.GetBytes(encoded));
            }
            catch (JsonException)
            {
                subscription = null;
            }

            if (subscription is null)
            {
                await RaskCqrsEndpointExtensions.ProblemAsync(context, StatusCodes.Status400BadRequest,
                    "Malformed subscription",
                    $"'{contract.Name}' carries what it watches in the "
                    + $"'{RemoteEndpointDefaults.MessageQueryParameter}' parameter, as JSON.")
                    .ConfigureAwait(false);
                return;
            }
        }
        else if (encoded.Length > 0)
        {
            await RaskCqrsEndpointExtensions.ProblemAsync(context, StatusCodes.Status400BadRequest,
                "Malformed subscription",
                $"'{contract.Name}' goes to every subscriber, so it takes nothing to watch.").ConfigureAwait(false);
            return;
        }

        var dispatcher = context.RequestServices.GetService<LocalDispatcher>()
                         ?? throw new InvalidOperationException("MapRaskCqrs() needs AddRaskCqrsServer() during startup.");
        await StreamAsync(context, dispatcher, contract, subscription, options).ConfigureAwait(false);
    }

    private static async Task StreamAsync(
        HttpContext context,
        LocalDispatcher dispatcher,
        RemoteContract contract,
        object? subscription,
        RaskCqrsServerOptions options)
    {
        var aborted = context.RequestAborted;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = dispatcher
            .Watch(contract.MessageType, subscription, () => ready.TrySetResult(), aborted)
            .GetAsyncEnumerator(aborted);
        var step = new Step(notifications);
        try
        {
            await WriteAsync(context, contract, step, ready.Task, options.EventKeepAlive).ConfigureAwait(false);
        }
        finally
        {
            // An async iterator refuses to be disposed mid-step, and the request's abort is what ends the step the
            // loop left pending — so it is drained first, whatever it ended with.
            try
            {
                await step.Next.ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Only draining: the step's outcome was already handled, or no longer matters.
            catch (Exception)
#pragma warning restore CA1031
            {
            }

            await notifications.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteAsync(
        HttpContext context,
        RemoteContract contract,
        Step step,
        Task ready,
        TimeSpan keepAlive)
    {
        var aborted = context.RequestAborted;
        var replayed = false;
        try
        {
            // The policy runs inside the first step, before anything is listening — so a refusal is still a status
            // code rather than a stream that closes for no reason.
            // Admitted is either signal: "connected", or a first notification — the replay comes before "connected".
            await Task.WhenAny(ready, step.Next).ConfigureAwait(false);
            if (step.Next.IsCompleted)
            {
                if (!await step.Next.ConfigureAwait(false))
                {
                    return;
                }

                replayed = true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            await RaskCqrsEndpointExtensions.ProblemAsync(context, StatusCodes.Status403Forbidden, "Forbidden", null)
                .ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            return;
        }

        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers[HeaderNames.CacheControl] = "no-store";

        // A reverse proxy that buffers the response holds every event until the buffer fills — for a stream that is
        // "never". nginx reads this header; ASP.NET's own buffering (response compression included) is turned off.
        response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            // "ready" goes first on the wire even when a replay came first here: the client marks itself live on it,
            // then takes the replay as its first value — the same order it would see in-process.
            await response.Body.WriteAsync(ReadyFrame, aborted).ConfigureAwait(false);
            await response.Body.FlushAsync(aborted).ConfigureAwait(false);

            while (true)
            {
                if (!replayed)
                {
                    bool more;
                    try
                    {
                        more = await step.Next.WaitAsync(keepAlive, aborted).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        await response.Body.WriteAsync(KeepAliveFrame, aborted).ConfigureAwait(false);
                        await response.Body.FlushAsync(aborted).ConfigureAwait(false);
                        continue;
                    }

                    if (!more)
                    {
                        return;
                    }
                }

                replayed = false;

                // The codec writes compact JSON — a string's newlines are escaped — so one event is one data line.
                await response.Body.WriteAsync(DataPrefix, aborted).ConfigureAwait(false);
                await response.Body.WriteAsync(NotificationWire.EncodeEvent(contract, step.Current), aborted)
                    .ConfigureAwait(false);
                await response.Body.WriteAsync(FrameEnd, aborted).ConfigureAwait(false);
                await response.Body.FlushAsync(aborted).ConfigureAwait(false);

                step.Advance();
            }
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The client went away, which is how every subscription ends.
        }
    }

    // The enumerator and its one pending step, so the caller can drain whatever step the writer left in flight.
    private sealed class Step(IAsyncEnumerator<INotification> notifications)
    {
        public Task<bool> Next { get; private set; } = notifications.MoveNextAsync().AsTask();

        public INotification Current => notifications.Current;

        public void Advance() => Next = notifications.MoveNextAsync().AsTask();
    }
}
