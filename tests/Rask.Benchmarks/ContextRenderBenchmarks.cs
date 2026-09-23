using BenchmarkDotNet.Attributes;
using Rask.Core;
using Rask.Core.Components;

namespace Rask.Benchmarks;

// A live-root render of a page that provides context and reads it back: two providers (one named) around 50 user
// components, each reading through Get, Required and Has. Measures the context push in HtmlSerializer and the readers'
// consumer mark — the two places the devtools hook context, and the only render path no other benchmark reaches.
[MemoryDiagnoser]
public partial class ContextRenderBenchmarks : global::Rask.Core.RaskMarkup
{
    private Component _tree = null!;

    [IterationSetup]
    public void IterationSetup() => _tree = BuildTree();

    [Benchmark]
    public string RenderContextConsumersTenTimes()
    {
        string? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = _tree.RenderAsLiveRoot();
        }

        return last!;
    }

    private static Component BuildTree()
    {
        var rows = new List<Component>(50);
        for (var i = 0; i < 50; i++)
        {
            rows.Add(ContextReaderRow.Key(i).Index(i));
        }

        return [
            Doctype,
            Document[
                Body[
                    Context.Provide(new BenchTheme("dark"))[
                        Context.Provide(42, Name: "page-size")[
                            Div.Class("rows")[rows]
                        ]
                    ]
                ]
            ]
        ];
    }
}

public sealed record BenchTheme(string Name);

// Reads every way a component can: the value it renders, a required one, and a presence check.
public sealed partial class ContextReaderRow : Component
{
    public int Index { get; set; }

    protected override Component? Render()
    {
        var theme = Context.Get<BenchTheme>();
        var pageSize = Context.Required<int>("page-size");
        var hasUser = Context.Has<string>("user");
        return Div.Class(theme?.Name)[$"{Index}/{pageSize}{(hasUser ? "+" : "")}"];
    }
}
