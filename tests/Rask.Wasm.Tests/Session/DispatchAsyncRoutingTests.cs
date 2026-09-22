using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

[Collection("WasmSession")]
// Asserts against the `html` payload field — force the legacy full-HTML wire shape (framework
// default is LiveDiffMode.Auto). The WasmSession collection serializes these tests so the
// static-field write is safe.
public class DispatchAsyncRoutingTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task Dispatching_empty_json_returns_empty_bytes()
    {
        var (session, _) = NewSession(diffMode: DiffMode);

        var result = await session.DispatchAsync(Array.Empty<byte>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatching_a_navigate_routes_to_the_navigate_branch_and_updates_the_route_state()
    {
        var (session, services) = NewSession(diffMode: DiffMode);
        var routeState = services.GetRequiredService<RouteState>();
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/destination","query":""}"""));

        Assert.NotEmpty(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("/destination", routeState.Path);
        var history = doc.RootElement.GetProperty("history");
        Assert.Equal("push", history.GetProperty("action").GetString());
        Assert.Equal("/destination", history.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Dispatching_a_navigate_to_an_empty_path_returns_empty()
    {
        var (session, _) = NewSession(diffMode: DiffMode);

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":""}"""));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatching_a_navigate_with_replace_emits_a_history_replace()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/x","replace":true}"""));

        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("replace", doc.RootElement.GetProperty("history").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Dispatching_with_no_handler_id_and_no_type_returns_empty()
    {
        var (session, _) = NewSession(diffMode: DiffMode);

        var result = await session.DispatchAsync(Utf8("""{"foo":"bar"}"""));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatching_an_unknown_handler_id_returns_empty()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"id":"h999"}"""));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Dispatching_a_known_handler_returns_a_payload_with_the_updated_html()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        var result = await session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}","type":"click"}"""));

        Assert.NotEmpty(result);
        using var doc = JsonDocument.Parse(result.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("count=1", html);
    }

    [Fact]
    public async Task Dispatching_a_navigate_with_an_empty_query_puts_no_question_mark_in_the_history_url()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        await session.InitialRenderAsync();

        var result = await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/x","query":""}"""));

        using var doc = JsonDocument.Parse(result.AsMemory());
        Assert.Equal("/x", doc.RootElement.GetProperty("history").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Concurrent_dispatches_are_serialised_by_the_lock()
    {
        var (session, _) = NewSession(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}","type":"click"}""")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var counts = results.Select(r =>
        {
            using var doc = JsonDocument.Parse(r.AsMemory());
            var html = doc.RootElement.GetProperty("html").GetString()!;
            return int.Parse(Regex.Match(html, "count=(\\d+)").Groups[1].Value);
        }).OrderBy(c => c).ToArray();

        // Lock guarantees 5 distinct sequential counts 1..5.
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, counts);
    }

    [Fact]
    public async Task A_dispatched_handler_that_throws_shows_the_error_page_and_the_session_stays_usable()
    {
        var (session, _) = NewSession<ThrowingStubApp>(diffMode: DiffMode);
        var initial = await session.InitialRenderAsync();
        var handlerId = MarkupAssert.FirstHandlerId(initial);

        var result = await session.DispatchAsync(Utf8($$"""{"id":"{{handlerId}}"}"""));

        // The host wraps the app in a RootErrorBoundary, so a handler that throws paints the error page
        // rather than dropping the frame — the browser has to be told something happened.
        Assert.Contains("Something went wrong", Encoding.UTF8.GetString(result));

        // Session is still usable: a subsequent dispatch for an unknown handler id returns empty,
        // proving the lock was released and the session didn't crash.
        var follow = await session.DispatchAsync(Utf8("""{"id":"h999"}"""));

        Assert.Empty(follow);
    }
}

[CollectionDefinition("WasmSession", DisableParallelization = true)]
public class WasmSessionCollection
{
}
