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
        // The native case: no ambient cookie, so the app puts its bearer token on every request.
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
        var file = RemoteFile.FromBytes("a.png", "image/png", [1, 2, 3]);

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

    private static IDispatcher Dispatcher(FakeHandler handler, Action<RaskCqrsClientOptions>? configure = null)
    {
        var services = new ServiceCollection();
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
