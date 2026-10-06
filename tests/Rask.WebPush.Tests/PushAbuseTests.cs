using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.WebPush.Tests;

// #1176: the subscribe route is open to anyone, every new endpoint is a row in the app's own database, and a
// send is this server's own POST to whatever was stored. So what can be stored, how fast, and where a send
// may land are all bounded — and one endpoint that never answers must not hold a broadcast.
public sealed class PushAbuseTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.10")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("100.64.0.1")]      // carrier-grade NAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void A_name_that_resolves_inside_the_network_is_not_a_push_service(string address)
    {
        var reachable = PushConnection.IsPublic(IPAddress.Parse(address));

        Assert.False(reachable);
    }

    [Theory]
    [InlineData("142.250.74.14")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("192.169.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2a00:1450:4001:81b::200e")]
    public void A_public_address_is_reachable(string address)
    {
        var reachable = PushConnection.IsPublic(IPAddress.Parse(address));

        Assert.True(reachable);
    }

    [Fact]
    public async Task A_send_to_a_name_that_resolves_to_this_machine_never_connects()
    {
        // The stored-subscription check reads the NAME. This is the address, after resolution, at connect.
        using var http = new HttpClient(PushConnection.Handler());

        var refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => http.GetAsync(new Uri("http://localhost:9/"), TestContext.Current.CancellationToken));

        Assert.Contains("does not resolve to a public address", refused.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_sender_refuses_redirects_and_private_addresses_however_it_was_registered(bool battery)
    {
        // The battery's registration left the handler at its defaults, so the path every scaffolded app takes
        // still followed redirects while the docs said nothing did.
        var services = new ServiceCollection();
        Action<WebPushOptions> keys = o =>
        {
            o.VapidKeys = VapidKeys.Generate();
            o.Subject = "mailto:ops@example.com";
        };
        _ = battery ? services.AddRaskWebPush<PushDbContext>(keys) : services.AddRaskWebPush(keys);
        using var provider = services.BuildServiceProvider();

        var handler = Innermost(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IWebPush)));
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IWebPush));

        Assert.False(handler.AllowAutoRedirect);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Equal(TimeSpan.FromSeconds(10), http.Timeout);
    }

    [Fact]
    public async Task The_table_holds_only_so_many_signed_out_subscriptions()
    {
        await using var harness = new PushHarness(configure: o => o.MaxAnonymousSubscribers = 2);
        await harness.Push.Subscribe(PushHarness.Browser("a"), TestContext.Current.CancellationToken);
        await harness.Push.Subscribe(PushHarness.Browser("b"), TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => harness.Push.Subscribe(PushHarness.Browser("c"), TestContext.Current.CancellationToken));
        await harness.Push.Subscribe(PushHarness.Browser("a"), TestContext.Current.CancellationToken);

        Assert.Contains("MaxAnonymousSubscribers", refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, harness.Rows());
    }

    [Fact]
    public async Task A_broadcast_sends_to_several_subscribers_at_once()
    {
        // In a row, N endpoints that never answer held a send for N timeouts.
        var sender = new OverlappingSender();
        await using var harness = new PushHarness(sender: sender);
        for (var i = 0; i < 4; i++)
        {
            await harness.Push.Subscribe(PushHarness.Browser($"device-{i}"), TestContext.Current.CancellationToken);
        }

        var delivered = await harness.Push.Deliver(
            new WebPushMessage { Title = "hi" }, userId: null, TestContext.Current.CancellationToken);

        Assert.Equal(4, delivered);
        Assert.True(sender.Overlapped, "no two sends were ever in flight together");
    }

    [Fact]
    public async Task A_subscription_whose_key_is_not_on_the_curve_is_dropped_without_failing_the_broadcast()
    {
        // The key is the right length, so it is stored; that it is no point on the curve only shows when it
        // is used. Anyone can post one, and left to escape it would fail every send and never be removed.
        var sender = new RejectingSender("https://push.example/forged");
        await using var harness = new PushHarness(sender: sender);
        await harness.Push.Subscribe(PushHarness.Browser("phone"), TestContext.Current.CancellationToken);
        await harness.Push.Subscribe(PushHarness.Browser("forged"), TestContext.Current.CancellationToken);

        var delivered = await harness.Push.Deliver(
            new WebPushMessage { Title = "hi" }, userId: null, TestContext.Current.CancellationToken);

        Assert.Equal(1, delivered);
        Assert.Equal(1, harness.Rows());
    }

    [Fact]
    public async Task The_subscribe_endpoint_answers_401_to_a_visitor_when_the_app_requires_a_user()
    {
        await using var app = await HostAsync(o => o.RequireUser = true);
        using var http = app.GetTestClient();

        var response = await PostAsync(http, "a");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_subscribe_endpoint_answers_429_once_the_table_is_full()
    {
        await using var app = await HostAsync(o => o.MaxAnonymousSubscribers = 1);
        using var http = app.GetTestClient();

        var first = await PostAsync(http, "a");
        var second = await PostAsync(http, "b");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task The_subscribe_endpoint_answers_429_to_a_client_that_keeps_posting()
    {
        await using var app = await HostAsync();
        using var http = app.GetTestClient();
        for (var i = 0; i < 10; i++)
        {
            await PostAsync(http, $"device-{i}");
        }

        var eleventh = await PostAsync(http, "one-more");

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string device)
    {
        var browser = PushHarness.Browser(device);
        return http.PostAsJsonAsync(
            "/_rask/push/subscribe",
            new { endpoint = browser.Endpoint, p256dh = browser.P256dh, auth = browser.Auth },
            cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<PushHost> HostAsync(Action<WebPushOptions>? configure = null)
    {
        var database = Path.Combine(Path.GetTempPath(), $"rask-push-abuse-{Guid.NewGuid():N}.db");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRaskWebPush<PushDbContext>(configure);
        builder.Services.AddDbContextFactory<PushDbContext>(o => o.UseSqlite($"Data Source={database}"));
        var app = builder.Build();
        app.MapRaskPush();

        await using (var db = await app.Services.GetRequiredService<IDbContextFactory<PushDbContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        }

        await app.StartAsync(TestContext.Current.CancellationToken);
        return new PushHost(app, database);
    }

    private sealed class PushHost(WebApplication app, string database) : IAsyncDisposable
    {
        public HttpClient GetTestClient() => app.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            await app.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    private static SocketsHttpHandler Innermost(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler { InnerHandler: { } inner })
        {
            handler = inner;
        }

        return Assert.IsType<SocketsHttpHandler>(handler);
    }

    // Fails one endpoint the way the encryptor does for a key that is no point on the curve.
    private sealed class RejectingSender(string forged) : IWebPush
    {
        public Task<WebPushResult> Send(
            PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default) =>
            string.Equals(subscription.Endpoint, forged, StringComparison.Ordinal)
                ? throw new System.Security.Cryptography.CryptographicException("The specified key is not valid.")
                : Task.FromResult(new WebPushResult(WebPushStatus.Success, 201));
    }

    // Answers only once a second send has started, so it completes at all only when sends overlap.
    private sealed class OverlappingSender : IWebPush
    {
        private readonly TaskCompletionSource _second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;

        public bool Overlapped => _second.Task.IsCompletedSuccessfully;

        public async Task<WebPushResult> Send(
            PushSubscription subscription, WebPushMessage message, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _started) >= 2)
            {
                _second.TrySetResult();
            }

            await _second.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return new WebPushResult(WebPushStatus.Success, 201);
        }
    }
}
