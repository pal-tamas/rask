using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Api.Client.Tests;

/// <summary>
///     The same claim as <see cref="RoundTripTests" />, for the other front door: minimal API endpoints,
///     called through the client generated from the <c>MapGet</c>/<c>MapPost</c> calls themselves.
/// </summary>
/// <remarks>
///     The endpoints are declared in <see cref="WidgetEndpoints" /> below, in ordinary minimal API code —
///     the generator reads those very invocations. Grouping is by route rather than by declaring type,
///     because a minimal API has no controller to be named after and most of them live in a
///     <c>Program.cs</c> whose enclosing type is <c>Program</c>.
/// </remarks>
public sealed class MinimalApiRoundTripTests : IAsyncLifetime
{
    private IHost _host = null!;
    private WidgetsClient _widgets = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(WidgetEndpoints.Map);
                }))
            .StartAsync();

        _widgets = new WidgetsClient(_host.GetTestClient(), new ApiClientOptions());
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_route_parameter_reaches_the_right_endpoint()
    {
        var widget = await _widgets.GetById(4);

        Assert.NotNull(widget);
        Assert.Equal(4, widget.Id);
        Assert.Equal("widget-4", widget.Name);
    }

    [Fact]
    public async Task A_query_parameter_binds()
    {
        var widget = await _widgets.Get(9);

        Assert.NotNull(widget);
        Assert.Equal("page-9", widget.Name);
    }

    [Fact]
    public async Task A_request_body_round_trips()
    {
        var created = await _widgets.Post(new Widget(41, "answer"));

        Assert.NotNull(created);
        Assert.Equal(42, created.Id);
        Assert.Equal("answer", created.Name);
    }

    [Fact]
    public async Task A_Results_union_takes_its_client_type_from_the_alternative_carrying_a_body()
    {
        // Results<Ok<string>, NotFound> is the shape an author reaches for when they want both a typed
        // body and a real 404. Without reading it, the whole TypedResults style — the one Microsoft
        // recommends — would report as having no statically known response type and get no client.
        var name = await _widgets.GetByIdName(5);

        Assert.Equal("widget-5", name);
    }

    [Fact]
    public async Task WithName_decides_the_client_method_name()
    {
        // The method is called Untag, not DeleteByIdTag. If the derived name had won this would not
        // compile, which is the assertion.
        await _widgets.Untag(3);
    }
}

/// <summary>A widget, as the minimal API sends it.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Name">Its name.</param>
public sealed record Widget(int Id, string Name);

/// <summary>
///     The endpoints under test — ordinary minimal API code. The generator reads these very invocations
///     at compile time, and the app maps them at run time: one declaration serving both.
/// </summary>
public static class WidgetEndpoints
{
    /// <summary>Maps them.</summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // TypedResults, so the response type is in the signature and the client is generated from it.
        endpoints.MapGet(
            "/api/widgets/{id:int}",
            (int id) => TypedResults.Ok(new Widget(id, "widget-" + id)));

        // A plain return value, which minimal APIs serialize as JSON.
        endpoints.MapGet("/api/widgets", (int page) => new Widget(page, "page-" + page));

        endpoints.MapPost("/api/widgets", (Widget body) => new Widget(body.Id + 1, body.Name));

        // Two alternatives: the Ok<T> supplies the client's return type, the NotFound is a real 404.
        endpoints.MapGet(
            "/api/widgets/{id:int}/name",
            Results<Ok<string>, NotFound> (int id) =>
                id > 0 ? TypedResults.Ok("widget-" + id) : TypedResults.NotFound());

        // An endpoint answering nothing, and .WithName winning over the derived method name.
        endpoints.MapDelete("/api/widgets/{id:int}/tag", (int id) => TypedResults.NoContent())
            .WithName("Untag");
    }
}
