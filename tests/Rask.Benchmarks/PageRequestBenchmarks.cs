using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server;

#pragma warning disable RASK019 // a benchmark page has nothing to put in <head> beyond its title

namespace Rask.Benchmarks;

// One GET for a routed page, through the whole ASP.NET pipeline and UseRask — the path a first visit, a
// crawler and a link preview all take.
//
// Nothing else measures it. Every render benchmark starts below the handler, so a change to what the
// handler does per request — resolving the route, evaluating the guard, building a session, rendering,
// writing the document, choosing cache headers — moves no number anywhere else.
//
// In memory rather than over a socket: the handler's cost is what is under measurement, and a socket
// would add the same noise to both sides of every comparison. Every page is live, so each GET builds a
// session the benchmark never connects; the grace period releases it quickly, so a run of thousands of
// requests measures requests rather than a store filling up.
[MemoryDiagnoser]
public partial class PageRequestBenchmarks
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    [GlobalSetup]
    public void Setup()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        // Nothing the pipeline logs belongs in a benchmark's numbers.
        builder.Logging.ClearProviders();
        builder.Services.AddRouting();
        builder.Services.AddRask(configureServer: o =>
            o.UnconnectedSessionGracePeriod = TimeSpan.FromMilliseconds(50));

        _app = builder.Build();
        _app.UseRouting();
        _app.UseWebSockets();
        _app.UseRask<RequestApp>();
        _app.StartAsync().GetAwaiter().GetResult();

        _client = _app.GetTestServer().CreateClient();

        // Warmed: the route table, the type caches and the scoped-asset bundle are paid for once per
        // process, and are not what a steady-state request costs.
        using var warm = _client.GetAsync(RequestPage.Path).GetAwaiter().GetResult();
        warm.EnsureSuccessStatusCode();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Benchmark]
    public async Task<int> GetPage()
    {
        using var response = await _client.GetAsync(RequestPage.Path);
        var body = await response.Content.ReadAsByteArrayAsync();
        return body.Length;
    }

    public sealed partial class RequestApp : Component
    {
        protected override Component? HeadAssets => Title["page-request"];

        protected override Component? Render() => Router;
    }

    [Route(Path)]
    public sealed partial class RequestPage : Component
    {
        public const string Path = "/benchmarks/page-request";

        private const int Rows = 20;

        protected override Component? Render()
        {
            var rows = new List<Component>(Rows);
            for (var i = 0; i < Rows; i++)
            {
                rows.Add(Div.Class("line").Id($"r{i}").Key(i)[Span.Class("label")[$"Item {i}"]]);
            }

            return Div.Class("wrap")[rows];
        }
    }
}
