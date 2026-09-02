using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Api.Client.Tests;

/// <summary>
///     The generated client, called against the real controllers it was generated from.
/// </summary>
/// <remarks>
///     <para>
///         This is the suite that matters. A generator test that asserts the emitted text compiles proves
///         the client <em>builds</em>, not that it addresses the right URL — and a client that builds the
///         <em>almost</em> right URL type-checks on both sides and fails as a 404 in production, which is
///         the worst outcome this feature has available. So every test here stands up the actual
///         controllers under a real server and calls them through the generated client.
///     </para>
///     <para>
///         The equivalent lesson is written down in Rask.Cqrs.Transport.Tests: two halves being green
///         separately proves nothing about the seam between them.
///     </para>
/// </remarks>
public sealed class RoundTripTests : IAsyncLifetime
{
    private IHost _host = null!;
    private PostsClient _posts = null!;
    private HealthClient _health = null!;
    private PostStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new PostStore();

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_store);
                    services.AddRaskApi();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapRaskApi());
                }))
            .StartAsync();

        var http = _host.GetTestClient();
        var options = new ApiClientOptions();

        // Constructed directly rather than through AddRaskApiClient, so a failure here is the generated
        // code rather than the DI wiring. AddRaskApiClient has its own test below.
        _posts = new PostsClient(http, options);
        _health = new HealthClient(http, options);
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_route_parameter_reaches_the_right_resource()
    {
        var post = await _posts.Get(2);

        Assert.NotNull(post);
        Assert.Equal(2, post.Id);
        Assert.Equal("second", post.Title);
        Assert.Equal(["intro", "deep"], post.Tags);
    }

    [Fact]
    public async Task A_query_parameter_arrives_as_the_server_reads_it()
    {
        // Asserted through the server's own observation rather than the response: the point is that the
        // value bound, and a response echoing it could be right for the wrong reason.
        _ = await _posts.List(page: 7);

        Assert.Equal(7, _store.LastPageAsked);
    }

    [Fact]
    public async Task An_omitted_optional_parameter_leaves_the_server_default_standing()
    {
        // ?page= and no page at all are different requests: the binder reads the first as present-and-
        // blank. A null must be omitted, not sent empty, or the action's own default never applies.
        _ = await _posts.List();

        Assert.Equal(1, _store.LastPageAsked);
    }

    [Fact]
    public async Task A_collection_result_round_trips()
    {
        var posts = await _posts.List(1);

        Assert.NotNull(posts);
        Assert.Equal(2, posts.Count);
        Assert.Contains(posts, p => p.Title == "first");
    }

    [Fact]
    public async Task A_request_body_round_trips()
    {
        var created = await _posts.Create(new NewPost("third", ["fresh"]));

        Assert.NotNull(created);
        Assert.Equal("third", created.Title);
        Assert.Equal(["fresh"], created.Tags);
        Assert.Equal("third", _store.Find(created.Id)?.Title);
    }

    [Fact]
    public async Task A_void_action_answers_without_a_body()
    {
        await _posts.Remove(1);

        Assert.Null(_store.Find(1));
    }

    [Fact]
    public async Task An_injected_service_is_not_a_client_parameter()
    {
        // [FromServices] comes from the container. If it reached the signature, this would not compile —
        // which is the assertion. The call proves the endpoint still works with it filtered out.
        var title = await _posts.Title(1);

        Assert.Equal("first", title);
    }

    [Fact]
    public async Task A_controller_token_in_the_route_resolves()
    {
        var status = await _health.Get();

        Assert.Equal("ok", status);
    }

    [Fact]
    public async Task A_failure_status_arrives_as_an_ApiException_carrying_it()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() => _posts.Get(404));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal("GET", error.Method);
        Assert.Contains("/api/posts/404", error.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_never_arrives_is_distinguishable_from_one_the_server_refused()
    {
        // A null StatusCode means the call never reached a server. Blurring the two makes "is it down or
        // am I wrong?" unanswerable at a call site.
        using var unreachable = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") };
        var client = new PostsClient(unreachable, new ApiClientOptions());

        var error = await Assert.ThrowsAsync<ApiException>(() => client.Get(1));

        Assert.Null(error.StatusCode);
        Assert.NotNull(error.InnerException);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a%2Fb")]
    [InlineData("a?b=c")]
    [InlineData("a#b")]
    [InlineData("ä ö")]
    public async Task A_route_value_needing_escaping_survives_the_trip(string value)
    {
        // The failure this prevents is not a 404 but a *wrong request*: an unescaped "/" or "?" in a
        // segment changes which endpoint the call reaches, or turns part of the value into a query. Both
        // look like the server misbehaving from the call site.
        var echoed = await _health.Echo(value);

        Assert.Equal(value, echoed);
    }

    [Fact]
    public async Task A_required_member_is_still_enforced_by_the_lean_registration()
    {
        // AddRaskApi registers AddMvcCore().AddDataAnnotations() rather than AddControllers(), to keep
        // the API explorer, CORS services and formatter mappings out of an app that never asked for
        // them. DataAnnotations is kept because dropping it changes BEHAVIOUR rather than only weight —
        // this endpoint would start accepting what it used to reject, silently. That claim is only
        // worth making if something checks it.
        var error = await Assert.ThrowsAsync<ApiException>(() => _health.Checked(new Checked()));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task AddRaskApiClient_registers_every_generated_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_host.GetTestClient());
        services.AddRaskApiClient();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<PostsClient>());
        Assert.NotNull(scope.ServiceProvider.GetService<HealthClient>());
    }
}
