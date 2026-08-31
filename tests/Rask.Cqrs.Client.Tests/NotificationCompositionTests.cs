using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Client.Tests;

/// <summary>
///     Notifications are the one message kind a client does not simply hand to the server. They fan out,
///     so a client's own reactors — a badge, a toast — must still run, and the notification must also
///     travel. Both halves, and exactly once each.
/// </summary>
public sealed class NotificationCompositionTests
{
    [Fact]
    public async Task A_published_notification_runs_the_clients_own_handler_and_still_travels()
    {
        var handler = new CountingHandler();
        var dispatcher = Dispatcher(handler);

        await dispatcher.PublishAsync(new ThingArchived(4242));

        Assert.Contains(4242, ThingArchivedReactor.Seen);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Registering_the_client_twice_does_not_send_a_notification_twice()
    {
        // The re-registration guard is per ServiceCollection, but the invoker registry it installs into
        // is static and process-wide. A second registration — a test, a rebuilt container, a host that
        // composes two collections — must not wrap the composed invoker again and turn one publish into
        // two sends.
        _ = Dispatcher(new CountingHandler());

        var handler = new CountingHandler();
        var dispatcher = Dispatcher(handler);

        await dispatcher.PublishAsync(new ThingArchived(99));

        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task A_LocalOnly_message_still_runs_in_the_client()
    {
        // The documented escape hatch, and the only one: a client is otherwise a PURE client, so a handler
        // sitting beside the call site would be bypassed and the server would answer 404 for a name it has
        // no handler for - a failure that reads as a transport problem rather than a design decision.
        var handler = new CountingHandler();
        var dispatcher = Dispatcher(handler);

        var total = await dispatcher.SendAsync(new IncrementLocalCounter(5));

        Assert.True(total >= 5, "the local handler did not run");
        Assert.Equal(0, handler.Requests);
    }

    private static IDispatcher Dispatcher(CountingHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") });
        services.AddRaskCqrsClient();
        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
        }
    }
}
