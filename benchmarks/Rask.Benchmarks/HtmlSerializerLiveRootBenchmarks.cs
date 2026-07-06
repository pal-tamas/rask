using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using C = Rask.Core.Components.Generated;

namespace Rask.Benchmarks;

// Full-page live-root render. Isolates the per-render cost of the framework-managed <head>/<body>
// path in HtmlSerializer that a plain ToHtml() (no live context) never hits:
//   * GetService<IRaskHeadContribution>() and GetService<IRaskRuntimeScript>() resolved on EVERY
//     render (2b caches these on the live context instead), and
//   * the per-element LiveRenderContext.CurrentSync ThreadStatic read + _shellTags HashSet lookup.
// A small and a large page so the fixed two-lookup DI cost is visible at small sizes and amortised
// across many elements at large sizes.
[MemoryDiagnoser]
public class HtmlSerializerLiveRootBenchmarks
{
    private Component _small = null!;
    private Component _large = null!;
    private IServiceProvider _services = null!;

    [GlobalSetup]
    public void Setup()
    {
        _services = new ServiceCollection()
            .AddSingleton<IRaskHeadContribution, BenchHeadContribution>()
            .AddSingleton<IRaskRuntimeScript, BenchRuntimeScript>()
            .BuildServiceProvider();

        _small = BuildPage(20);
        _large = BuildPage(500);
    }

    [Benchmark]
    public string RenderPageSmall() => _small.RenderAsLiveRoot(_services);

    [Benchmark]
    public string RenderPageLarge() => _large.RenderAsLiveRoot(_services);

    private static Component BuildPage(int rowCount)
    {
        var rows = new List<Component>(rowCount);
        for (var i = 0; i < rowCount; i++)
        {
            rows.Add(C.Div(Class: "row", Id: $"r{i}", Key: i)[
                C.Span(Class: "label")[$"Item {i}"]
            ]);
        }

        return
        [
            C.Doctype(),
            C.Html()[
                // Head content is framework-managed (RASK019); the serializer's <head> branch emits
                // the head-asset sentinel and resolves IRaskHeadContribution regardless of children.
                C.Head(),
                C.Body()[
                    C.Div(Class: "container", Id: "root")[rows]
                ]
            ]
        ];
    }

    // Byte-stable head markup, built once (the real Server contribution is stable too, so the diff
    // codec never emits ops for it).
    private sealed class BenchHeadContribution : IRaskHeadContribution
    {
        private readonly Component _markup = C.Meta(Name: "theme-color", Content: "#0d1117");
        public Component? Render() => _markup;
    }

    private sealed class BenchRuntimeScript : IRaskRuntimeScript
    {
        private readonly Component _script = C.Script(Src: "/_rask/rask.js", Type: "module");
        public Component Render() => _script;
    }
}
