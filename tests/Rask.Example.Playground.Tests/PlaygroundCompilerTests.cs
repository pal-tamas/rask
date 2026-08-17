using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Example.Playground.Compiler;

namespace Rask.Example.Playground.Tests;

// Exercises the in-browser compile pipeline on the desktop runtime: real Roslyn, the real Rask
// ComponentFactoryGenerator, real Emit + Assembly.Load + render — only the metadata references come
// from files here instead of the browser's _framework/*.dll. If these pass, the browser host differs
// only in how it fetches those references.
public sealed class PlaygroundCompilerTests
{
    private static PlaygroundCompiler NewCompiler() =>
        new(TestReferences.Build(), new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Compiles_and_renders_a_simple_component()
    {
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            public sealed class Playground : Component
            {
                protected override Component? Render() =>
                    Div(Class: "greeting")["Hello from the playground"];
            }
            """);

        Assert.True(result.Succeeded, DumpDiagnostics(result));
        Assert.NotNull(result.Component);

        var html = result.Component!.ToHtml();
        Assert.Contains("<div", html);
        Assert.Contains("class=\"greeting\"", html);
        Assert.Contains("Hello from the playground", html);
    }

    [Fact]
    public async Task Runs_the_Rask_generator_so_user_component_factories_resolve()
    {
        // `Badge(...)` is the generated factory for the user's OWN component — it exists only if the Rask
        // source generator ran during this in-browser compile (the generator also emits the
        // `global using static Demo.Generated;` that brings the factory into scope). Without the generator
        // this is CS0103. Components live in a namespace, exactly as in a real Rask project.
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            namespace Demo;

            public sealed class Badge : Component
            {
                public required string Label { get; set; }
                protected override Component? Render() => Span(Class: "badge")[Label];
            }

            public sealed class Playground : Component
            {
                protected override Component? Render() =>
                    Div()[ Badge(Label: "new"), Badge(Label: "hot") ];
            }
            """);

        Assert.True(result.Succeeded, DumpDiagnostics(result));
        var html = result.Component!.ToHtml();
        Assert.Contains("<span class=\"badge\">new</span>", html);
        Assert.Contains("<span class=\"badge\">hot</span>", html);
    }

    [Fact]
    public async Task Renders_initial_state_of_a_stateful_component()
    {
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            public sealed class Playground : Component
            {
                private int _count = 5;
                protected override Component? Render() =>
                    Div(Id: "counter")[ $"Count: {_count}" ];
            }
            """);

        Assert.True(result.Succeeded, DumpDiagnostics(result));
        Assert.Contains("Count: 5", result.Component!.ToHtml());
    }

    [Fact]
    public async Task Surfaces_compiler_errors_and_does_not_run()
    {
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            public sealed class Playground : Component
            {
                protected override Component? Render() =>
                    Div()[ this_is_not_valid_csharp ];
            }
            """);

        Assert.False(result.Succeeded);
        Assert.Null(result.Component);
        Assert.Contains(result.Diagnostics, d => d.Severity == PlaygroundSeverity.Error && d.Id.StartsWith("CS"));
        // Diagnostics carry 1-based positions so they map straight onto Monaco markers.
        Assert.All(result.Diagnostics, d => Assert.True(d.StartLine >= 1 && d.StartColumn >= 1));
    }

    [Fact]
    public async Task Surfaces_Rask_analyzer_diagnostics_without_blocking_execution()
    {
        // `new Div()` compiles, but RASK014 (construct components via the factory) flags it — proving the
        // analyzer display-pass runs. It must NOT gate execution: the component still renders.
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;
            using Rask.Core.Components;
            using Rask.Html.Components;

            public sealed class Playground : Component
            {
                protected override Component? Render() => new Div();
            }
            """);

        Assert.True(result.Succeeded, DumpDiagnostics(result));
        Assert.Contains(result.Diagnostics, d => d.Id == "RASK014");
    }

    [Fact]
    public async Task Reports_a_friendly_message_when_no_component_is_defined()
    {
        var result = await NewCompiler().CompileAsync("""
            public static class Helper
            {
                public static int Add(int a, int b) => a + b;
            }
            """);

        Assert.False(result.Succeeded);
        Assert.Null(result.Component);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("component", StringComparison.OrdinalIgnoreCase));
    }

    private static string DumpDiagnostics(PlaygroundResult result) =>
        "Diagnostics:\n" + string.Join("\n",
            result.Diagnostics.Select(d => $"  {d.Severity} {d.Id} ({d.StartLine},{d.StartColumn}): {d.Message}"));
}
