using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Benchmarks.Infrastructure;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server;
using Rask.Server.Authentication;

#pragma warning disable RASK014 // a benchmark owns the root it dispatches into; there is no parent to build it

namespace Rask.Benchmarks;

/// <summary>
///     One click on the server, end to end: the frame's bytes in, the handler invoked under the route guard, the
///     render sent and the ack after it.
/// </summary>
/// <remarks>
///     <c>WsDispatchBenchmarks</c> stops at the parsed frame, <c>HandlerDispatchBenchmarks</c> starts at the
///     component and <c>LiveSessionSendBenchmarks</c> starts at the state change; nothing ran the endpoint's own
///     per-event work between them — the handler chain, the session lock, the guard before the handler and the one
///     after it. <see cref="RenderOnly" /> is the same page's render and send without a frame, so the difference
///     between the two is what the endpoint adds.
/// </remarks>
[MemoryDiagnoser]
public partial class EventDispatchBenchmarks
{
    private const int Rows = 20;
    private const int OtherRoutes = 20;

    // The two pages under test sit behind the rest of an app's table, as most of an app's pages do.
    private static readonly IReadOnlyList<Route> _routes =
    [
        .. Enumerable.Range(0, OtherRoutes).Select(i => new Route(typeof(OpenPage), $"/section{i}/{{id}}")),
        new Route(typeof(OpenPage), "/open"),
        new Route(typeof(MembersPage), "/members")
    ];

    private byte[] _frame = null!;
    private RaskServerLimits _limits = null!;
    private ServiceProvider _services = null!;
    private LiveSession _session = null!;
    private LiveSessionStore _store = null!;

    /// <summary>A page anyone may see, and one behind <c>[Authorize]</c> with a signed-in user on it.</summary>
    [Params("/open", "/members")]
    public string Path { get; set; } = "/open";

    [GlobalSetup]
    public void GlobalSetup()
    {
        // AddAuthorization's service logs, so the page behind [Authorize] needs a logger to be evaluated at all.
        _services = new ServiceCollection().AddLogging().AddRask().BuildServiceProvider();
        _store = _services.GetRequiredService<LiveSessionStore>();
        _limits = _services.GetRequiredService<RaskServerLimits>();
        _session = _store.Create(_ => new RootErrorBoundary(new EventRouterHost()));

        var route = _session.Services.GetRequiredService<RouteState>();
        route.Table = static () => _routes;
        route.Path = Path;
        _session.Services.GetRequiredService<SessionUserProvider>()
            .Set(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "ada")], "benchmark")));

        var html = _session.RenderInitialRoot();
        var socket = new NullWebSocket();
        _session.AttachSocket(socket, CancellationToken.None);

        var id = System.Text.RegularExpressions.Regex.Match(html, "data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        _frame = Encoding.UTF8.GetBytes($"{{\"id\":\"{id}\",\"seq\":1}}");

        // A click that reached no handler, or whose render never left, would measure an empty loop.
        Click();
        if (socket.BytesSent == 0 || Counter.Clicks != 1)
        {
            throw new InvalidOperationException($"the click on {Path} did not run its handler and send a frame: {html}");
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _store.RemoveAsync(_session.Id).GetAwaiter().GetResult();
        _services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Counter.Clicks = 0;
        Counter.Sheet = null;
    }

    /// <summary>The frame as the socket loop hands it over, through to the end of its dispatch.</summary>
    [Benchmark]
    public void Click()
    {
        using var doc = JsonDocument.Parse(_frame.AsMemory());
        var root = doc.RootElement;
        var hasType = root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String;

        RaskEndpointExtensions
            .ProcessFrameAsync(_session, root, hasType, type, _frame.Length, _store, _limits, _store.Metrics, CancellationToken.None)
            .GetAwaiter().GetResult();
        _session.LastHandlerTask.GetAwaiter().GetResult();
    }

    /// <summary>The same state change rendered and sent with no frame behind it.</summary>
    [Benchmark(Baseline = true)]
    public void RenderOnly()
    {
        Counter.Sheet!.Bump();
        _session.RequestRender().GetAwaiter().GetResult();
    }

    // One session per run, so the pages can share the count without a service to carry it.
    private static class Counter
    {
        public static int Clicks;
        public static Sheet? Sheet;
    }

    internal sealed partial class EventRouterHost : Component
    {
        protected override Component? Render() => Router.Routes(_routes);
    }

    internal sealed partial class OpenPage : Component
    {
        protected override Component? Render() => Sheet.Rows(Rows);
    }

    [Authorize]
    internal sealed partial class MembersPage : Component
    {
        protected override Component? Render() => Sheet.Rows(Rows);
    }

    internal sealed partial class Sheet : Component
    {
        public int Rows { get; set; }

        public void Bump()
        {
            Counter.Clicks++;
            StateHasChanged();
        }

        protected override Component? Render()
        {
            Counter.Sheet = this;
            var rows = new List<Component>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                rows.Add(Div.Class("line").Key(i)[Span.Class("label")[$"Item {i}"]]);
            }

            return Div.Class("wrap")[
                Button.OnClick(() => Counter.Clicks++)[$"clicked {Counter.Clicks}"],
                rows
            ];
        }
    }
}
