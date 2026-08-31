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

            public sealed partial class Playground : Component
            {
                protected override Component? Render() =>
                    Div.Class("greeting")["Hello from the playground"];
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
    public async Task Runs_the_Rask_generator_so_user_component_entries_resolve()
    {
        // `Badge.Label(...)` is the chain entry for the user's OWN component — it exists only if the Rask
        // source generator ran during this in-browser compile, because the generator emits that entry into
        // the markup host. Without the generator this is CS0103. Components live in a namespace, exactly as
        // in a real Rask project.
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            namespace Demo;

            public sealed partial class Badge : Component
            {
                public required string Label { get; set; }
                protected override Component? Render() => Span.Class("pill")[Label];
            }

            public sealed partial class Playground : Component
            {
                protected override Component? Render() =>
                    Div[ Badge.Label("new"), Badge.Label("hot") ];
            }
            """);

        Assert.True(result.Succeeded, DumpDiagnostics(result));
        var html = result.Component!.ToHtml();
        Assert.Contains("<span class=\"pill\">new</span>", html);
        Assert.Contains("<span class=\"pill\">hot</span>", html);
    }

    [Fact]
    public async Task Runs_the_Rask_generator_so_a_user_component_can_be_chained()
    {
        // The chain over the visitor's OWN component. `Badge.Message("new")` needs the generator's builder
        // ENTRY for `Badge`, which is emitted only when the driver is told `RaskBuilderSurface=true` — the
        // browser compile has no MSBuild to say it, so the driver has to. Without it there is no entry, the
        // name binds to the TYPE, and the step reads as a static member call: CS1955. `Div`/`Span` would
        // keep working either way (their entries are compiled into the referenced Rask.Core), so only a
        // component the visitor wrote proves the option actually arrived.
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            namespace Demo;

            public sealed partial class Badge : Component
            {
                public required string Message { get; set; }
                protected override Component? Render() => Span.Class("pill")[Message];
            }

            public sealed partial class Playground : Component
            {
                protected override Component? Render() =>
                    Div[ Badge.Message("new"), Badge.Message("hot") ];
            }
            """);

        Assert.True(result.Succeeded, DumpDiagnostics(result));
        var html = result.Component!.ToHtml();
        Assert.Contains("<span class=\"pill\">new</span>", html);
        Assert.Contains("<span class=\"pill\">hot</span>", html);
    }

    [Fact]
    public async Task Renders_initial_state_of_a_stateful_component()
    {
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;

            public sealed partial class Playground : Component
            {
                private int _count = 5;
                protected override Component? Render() =>
                    Div.Id("counter")[ $"Count: {_count}" ];
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

            public sealed partial class Playground : Component
            {
                protected override Component? Render() =>
                    Div[ this_is_not_valid_csharp ];
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
        // `new Div()` compiles, but RASK014 (construct components via the chain) flags it — proving the
        // analyzer display-pass runs. It must NOT gate execution: the component still renders.
        var result = await NewCompiler().CompileAsync("""
            using Rask.Core;
            using Rask.Core.Components;

            public sealed partial class Playground : Component
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
