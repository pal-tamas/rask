using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Client.Tests;

// Driven through the public surface — AddRaskCqrsClient, then IDispatcher — rather than by constructing
// the transport, so what is under test is what an app actually gets. The container's HttpClient is the
// seam: a browser client is expected to register one carrying its own origin, so a fake handler slots in
// exactly where a real browser's would.
public sealed class RemoteDispatchTests
{
    [Fact]
    public async Task A_query_travels_as_a_GET_with_the_message_in_the_url()
    {
        var handler = Handler(Json("""{"id":1,"name":"kettle"}"""));
        var result = await Dispatcher(handler).DispatchAsync(new GetThing(1));

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Contains("/_rask/cqrs/request/", handler.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("Rask.Cqrs.Client.Tests.GetThing", handler.Request.RequestUri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("m=", handler.Request.RequestUri.Query, StringComparison.Ordinal);
        Assert.Equal(new ThingDto(1, "kettle"), result);
    }

    [Fact]
    public async Task A_command_travels_as_a_POST_with_a_json_body()
    {
        var handler = Handler(new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(handler).DispatchAsync(new RenameThing(1, "pan"));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("\"name\":\"pan\"", handler.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_too_long_for_a_url_falls_back_to_POST_with_the_same_result()
    {
        // The fallback exists because a url ceiling differs per proxy: a query that only 414s in
        // production is the worst way to discover it. The result must be identical either way.
        var handler = Handler(Json("41"));
        var result = await Dispatcher(handler).DispatchAsync(new CountThings(new string('x', 4000)));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(41, result);
    }

    [Fact]
    public async Task Both_verbs_carry_the_header_that_makes_cross_site_markup_unable_to_trigger_them()
    {
        var get = Handler(Json("""{"id":1,"name":"a"}"""));
        await Dispatcher(get).DispatchAsync(new GetThing(1));
        Assert.True(get.Request!.Headers.Contains(RemoteEndpointDefaults.RequestHeader));

        var post = Handler(new HttpResponseMessage(HttpStatusCode.NoContent));
        await Dispatcher(post).DispatchAsync(new RenameThing(1, "b"));
        Assert.True(post.Request!.Headers.Contains(RemoteEndpointDefaults.RequestHeader));
    }

    [Fact]
    public async Task The_per_request_hook_can_attach_credentials()
    {
        // The token case: no ambient cookie, so the app puts its bearer token on every request.
        var handler = Handler(Json("""{"id":1,"name":"a"}"""));
        var dispatcher = Dispatcher(handler, o => o.ConfigureRequestAsync = (request, _) =>
        {
            request.Headers.Add("Authorization", "Bearer token-123");
            return Task.CompletedTask;
        });

        await dispatcher.DispatchAsync(new GetThing(1));

        Assert.Equal("Bearer token-123", handler.Request!.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task A_message_carrying_a_file_is_sent_as_multipart()
    {
        var handler = Handler(Json("\"ok\""));
        var file = new PickedFile("a.png", "image/png", [1, 2, 3]);

        await Dispatcher(handler).DispatchAsync(new AttachToThing(7, file));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.StartsWith("multipart/form-data", handler.Request.Content!.Headers.ContentType!.MediaType!, StringComparison.Ordinal);

        // The part is named by the index the JSON wrote, which is what pairs it back to its property.
        Assert.Contains("name=message", handler.Body!, StringComparison.Ordinal);
        Assert.Contains("name=0", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_carries_the_status_and_the_problem_document()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"type":"https://rask.dev/problems/forbidden","title":"Forbidden","detail":"not a member"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => Dispatcher(Handler(response)).DispatchAsync(new GetThing(1)));

        Assert.Equal(403, error.StatusCode);
        Assert.Equal("https://rask.dev/problems/forbidden", error.ProblemType);
        Assert.Equal("not a member", error.Detail);
        Assert.Equal("Rask.Cqrs.Client.Tests.GetThing", error.MessageName);
    }

    [Fact]
    public async Task A_request_that_never_reaches_the_server_reports_a_null_status()
    {
        // The null IS the signal — it is what separates "the server said no" from "there was no server".
        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => Dispatcher(Handler(new HttpRequestException("offline"))).DispatchAsync(new GetThing(1)));

        Assert.Null(error.StatusCode);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    /// <summary>
    ///     <c>options.Timeout</c> applies on the path a same-origin browser client actually takes.
    /// </summary>
    /// <remarks>
    ///     It did not (#893). <c>ResolveHttpClient</c> set <c>HttpClient.Timeout</c> only when it
    ///     constructed the client itself; a browser app hits the other branch and reuses the container's
    ///     <c>HttpClient</c>, whose <c>BaseAddress</c> is the page origin — so the option was accepted and
    ///     then disregarded on the default path, which is this repository's most expensive bug class.
    ///     <para>
    ///         The registration below deliberately leaves <c>HttpClient.Timeout</c> at its own default, as
    ///         a browser template's does — and the ELAPSED assertion is the load-bearing half. Without it
    ///         this test still passes against the bug, after ~100 s, on the client's own default timeout:
    ///         verified by reverting the fix, where it went green in 1 m 40 s instead of milliseconds.
    ///         Asserting only the exception would have been a test that passes for the wrong reason.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task The_configured_timeout_applies_to_a_client_the_app_registered()
    {
        using var handler = new HangingHandler();
        var started = System.Diagnostics.Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => Dispatcher(handler, o => o.Timeout = TimeSpan.FromMilliseconds(80))
                .DispatchAsync(new GetThing(1)));

        started.Stop();

        // Reported as "never reached the server", exactly as any other failure to arrive: the null status
        // is what separates it from an answer.
        Assert.Null(error.StatusCode);
        Assert.Equal("Rask.Cqrs.Client.Tests.GetThing", error.MessageName);

        // Generous against the 80 ms asked for, because this must not flake under a loaded gate — and
        // still an order of magnitude under the 100 s that HttpClient's own default would take, which is
        // the only thing this needs to separate.
        Assert.True(
            started.Elapsed < TimeSpan.FromSeconds(20),
            $"the configured 80 ms timeout was not what ended the request — it took {started.Elapsed}. "
            + "That is HttpClient's own default expiring instead, which is the bug (#893).");
    }

    [Fact]
    public async Task A_caller_cancelling_beats_the_timeout_and_still_propagates()
    {
        // The timeout must not swallow a cancellation the caller asked for — the linked source has to
        // keep the two distinguishable, or an unmounting component looks like a transport fault.
        using var handler = new HangingHandler();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Dispatcher(handler, o => o.Timeout = TimeSpan.FromMinutes(5))
                .DispatchAsync(new GetThing(1), cts.Token));
    }

    [Fact]
    public async Task A_cancellation_the_caller_asked_for_propagates_rather_than_being_reported_as_a_transport_failure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Dispatcher(Handler(Json("1"))).DispatchAsync(new CountThings("x"), cts.Token));
    }

    [Fact]
    public async Task A_file_result_arrives_as_a_readable_download_with_its_name_and_type()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent("id,name\n1,kettle"u8.ToArray()),
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        response.Content.Headers.ContentDisposition =
            new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "\"things.csv\"" };

        var download = await Dispatcher(Handler(response)).DispatchAsync(new ExportThings(2026));

        Assert.Equal("things.csv", download.FileName);
        Assert.Equal("text/csv", download.ContentType);

        using var reader = new StreamReader(download.OpenReadStream());
        Assert.Equal("id,name\n1,kettle", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Publishing_a_notification_reaches_the_server()
    {
        var handler = Handler(new HttpResponseMessage(HttpStatusCode.Accepted));
        await Dispatcher(handler).PublishAsync(new ThingRenamed(3));

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Contains("ThingRenamed", handler.Request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_without_anywhere_to_send_says_what_to_do_about_it()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrsClient();

        var error = Assert.Throws<InvalidOperationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IRemoteDispatch>());

        Assert.Contains("BaseAddress", error.Message, StringComparison.Ordinal);
    }

    private static IDispatcher Dispatcher(HttpMessageHandler handler, Action<RaskCqrsClientOptions>? configure = null)
    {
        var services = new ServiceCollection();

        // Timeout deliberately left at HttpClient's own default, the way a browser template's
        // registration does — so a test asserting options.Timeout cannot pass on the client's instead.
        services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") });
        services.AddRaskCqrsClient(configure);
        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    private static FakeHandler Handler(HttpResponseMessage response) => new(response, null);

    private static FakeHandler Handler(Exception failure) => new(null, failure);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>What a file input hands a component — the type a message declares, on every host.</summary>
    private sealed class PickedFile(string name, string contentType, byte[] bytes) : RaskFile
    {
        public override string Name => name;

        public override long Size => bytes.Length;

        public override string ContentType => contentType;

        public override DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public override Stream OpenReadStream(
            long maxAllowedSize = 512 * 1024,
            CancellationToken cancellationToken = default) =>
            bytes.Length > maxAllowedSize
                ? throw new IOException($"'{name}' is {bytes.Length} bytes, over the {maxAllowedSize} ceiling.")
                : new MemoryStream(bytes, writable: false);
    }

    /// <summary>A server that accepts the request and never answers — what a timeout is for.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Waits on the token rather than sleeping a fixed span, so the test finishes the moment
            // something cancels it and does not trade a real assertion for a timing race.
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class FakeHandler(HttpResponseMessage? response, Exception? failure) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;

            // Read here rather than in the test: the content is disposed with the request, so a test
            // reading it afterwards would see an empty body and quietly assert nothing.
            if (request.Content is not null)
            {
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return failure is not null ? throw failure : response!;
        }
    }
}
