namespace Rask.Cqrs.Transport.Tests;

// A subscription opened by the client half and served by the server half's event stream, over real HTTP: the client
// builds the GET, the server's routing, authentication and policy admit it, and the server's publishes come back as
// server-sent events the client decodes.
public sealed class SubscriptionWireTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_client_subscription_hears_what_the_server_publishes()
    {
        await using var wire = Wire.Connect();
        await using var stream = await OpenAsync<Announced>(wire);

        await wire.Server.PublishAsync(new Announced("hello"));

        Assert.Equal(new Announced("hello"), await stream.NextAsync());
    }

    [Fact]
    public async Task The_request_goes_to_the_events_route_with_the_key_in_the_query()
    {
        await using var wire = Wire.Connect();
        await using var stream = await OpenAsync<RoomMessage>(wire, key: 1);

        var sent = wire.Recorder.Last;

        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal(
            RemoteEndpointDefaults.RoutePrefix + "/" + RemoteEndpointDefaults.EventsSegment + "/"
            + Uri.EscapeDataString(Wire.Contract(new RoomMessage(1, "")).Name),
            sent.Uri.AbsolutePath);
        Assert.Equal("?for=1", sent.Uri.Query);
    }

    [Fact]
    public async Task A_scoped_subscription_hears_only_its_own_key()
    {
        await using var wire = Wire.Connect();
        await using var stream = await OpenAsync<RoomMessage>(wire, key: 1);

        await wire.Server.PublishAsync(new RoomMessage(2, "elsewhere"));
        await wire.Server.PublishAsync(new RoomMessage(1, "here"));

        Assert.Equal(new RoomMessage(1, "here"), await stream.NextAsync());
    }

    [Fact]
    public async Task The_server_s_policy_refuses_a_key_with_403()
    {
        await using var wire = Wire.Connect();

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(() => OpenAsync<RoomMessage>(wire, key: 2));

        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task A_new_subscription_starts_with_the_last_notification_published()
    {
        await using var wire = Wire.Connect();
        await using (await OpenAsync<Announced>(wire))
        {
            await wire.Server.PublishAsync(new Announced("first"));
            await wire.Server.PublishAsync(new Announced("latest"));
        }

        await using var late = await OpenAsync<Announced>(wire);

        Assert.Equal(new Announced("latest"), await late.NextAsync());
    }

    [Fact]
    public async Task An_unscoped_notification_that_declares_nothing_answers_404()
    {
        await using var wire = Wire.Connect();

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(() => OpenAsync<Undeclared>(wire));

        Assert.Equal(404, error.StatusCode);
    }

    [Fact]
    public async Task A_signed_out_caller_gets_401_unless_the_notification_allows_anonymous()
    {
        await using var wire = Wire.Connect(user: null);

        var refused = await Assert.ThrowsAsync<RemoteDispatchException>(() => OpenAsync<Announced>(wire));
        await using var open = await OpenAsync<PublicNotice>(wire);
        await wire.Server.PublishAsync(new PublicNotice("everyone"));

        Assert.Equal(401, refused.StatusCode);
        Assert.Equal(new PublicNotice("everyone"), await open.NextAsync());
    }

    [Fact]
    public async Task The_record_s_roles_are_enforced()
    {
        await using var guest = Wire.Connect(roles: "guest");
        await using var admin = Wire.Connect(roles: "admin");

        var refused = await Assert.ThrowsAsync<RemoteDispatchException>(() => OpenAsync<AdminNotice>(guest));
        await using var admitted = await OpenAsync<AdminNotice>(admin);

        Assert.Equal(403, refused.StatusCode);
    }

    [Fact]
    public async Task A_scoped_subscription_without_a_key_is_rejected_with_400()
    {
        await using var wire = Wire.Connect();
        var contract = Wire.Contract(new RoomMessage(1, ""));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            RemoteEndpointDefaults.RoutePrefix + "/" + RemoteEndpointDefaults.EventsSegment + "/" + Uri.EscapeDataString(contract.Name));
        request.Headers.Add(RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        request.Headers.Add("X-Test-User", "tester");

        using var response = await wire.Http.SendAsync(request);

        Assert.Equal(400, (int)response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_the_rask_header_is_refused_before_anything_else()
    {
        await using var wire = Wire.Connect();
        var contract = Wire.Contract(new Announced(""));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            RemoteEndpointDefaults.RoutePrefix + "/" + RemoteEndpointDefaults.EventsSegment + "/" + Uri.EscapeDataString(contract.Name));
        request.Headers.Add("X-Test-User", "tester");

        using var response = await wire.Http.SendAsync(request);

        Assert.Equal(400, (int)response.StatusCode);
    }

    // Opens the subscription and returns once the server has admitted it — the "ready" event — so a publish after this
    // is one the stream is already listening for.
    private static async Task<Stream<T>> OpenAsync<T>(Wire wire, object? key = null)
        where T : INotification
    {
        var stop = new CancellationTokenSource();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enumerator = wire.Subscriptions
            .Subscribe(Wire.Contract(Sample<T>()), key, () => admitted.TrySetResult(), stop.Token)
            .GetAsyncEnumerator(stop.Token);
        var next = enumerator.MoveNextAsync().AsTask();

        await Task.WhenAny(admitted.Task, next).WaitAsync(Wait);
        if (next.IsFaulted)
        {
            await next;
        }

        return new Stream<T>(enumerator, next, stop);
    }

    // A value of the type, only to find its contract by.
    private static INotification Sample<T>() => typeof(T).Name switch
    {
        nameof(Announced) => new Announced(""),
        nameof(AdminNotice) => new AdminNotice(""),
        nameof(PublicNotice) => new PublicNotice(""),
        nameof(Undeclared) => new Undeclared(""),
        nameof(RoomMessage) => new RoomMessage(0, ""),
        _ => throw new ArgumentOutOfRangeException(nameof(T)),
    };

    private sealed class Stream<T>(IAsyncEnumerator<INotification> enumerator, Task<bool> first, CancellationTokenSource stop)
        : IAsyncDisposable
    {
        private Task<bool> _next = first;

        public async Task<T> NextAsync()
        {
            Assert.True(await _next.WaitAsync(Wait));
            var value = (T)enumerator.Current;
            _next = enumerator.MoveNextAsync().AsTask();
            return value;
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            try
            {
                await _next;
            }
            catch (Exception ex) when (ex is OperationCanceledException or RemoteDispatchException or IOException)
            {
            }

            await enumerator.DisposeAsync();
            stop.Dispose();
        }
    }
}
