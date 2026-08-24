using Rask.Example.Playground.Compiler;

namespace Rask.Example.Playground.Tests;

// Exercises the as-you-type IDE brain on the desktop runtime: real Roslyn diagnostics without Emit, and real
// CompletionService IntelliSense over an AdhocWorkspace whose MEF host carries the Features assemblies. If
// these pass, the browser host differs only in where the metadata references come from (_framework/*.dll).
public sealed class PlaygroundWorkspaceTests
{
    private static PlaygroundWorkspace NewWorkspace() => new(TestReferences.Build());

    [Fact]
    public async Task DiagnoseAsync_surfaces_a_compiler_error_at_a_1_based_position()
    {
        var diagnostics = await NewWorkspace().DiagnoseAsync("""
            using Rask.Core;

            public sealed partial class Playground : Component
            {
                protected override Component? Render() =>
                    Div[ this_is_not_valid_csharp ];
            }
            """);

        var error = Assert.Single(diagnostics, d => d.Severity == PlaygroundSeverity.Error && d.Id.StartsWith("CS"));
        Assert.True(error.StartLine >= 1 && error.StartColumn >= 1);
    }

    [Fact]
    public async Task DiagnoseAsync_reports_no_errors_for_a_valid_snippet()
    {
        var diagnostics = await NewWorkspace().DiagnoseAsync("""
            using Rask.Core;

            namespace Demo;

            public sealed partial class Playground : Component
            {
                private int _count;

                protected override Component? Render() =>
                    Div.Class("card")[
                        P[$"Count: {_count}"],
                        Button.OnClick(() => _count++)["Click me"]
                    ];
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PlaygroundSeverity.Error);
    }

    [Fact]
    public async Task DiagnoseAsync_surfaces_Rask_analyzer_diagnostics()
    {
        // `new Div()` compiles but trips RASK014 (construct via the chain) — proof the analyzer
        // display-pass runs on the live path too, so framework hints squiggle as you type.
        var diagnostics = await NewWorkspace().DiagnoseAsync("""
            using Rask.Core;
            using Rask.Core.Components;

            public sealed partial class Playground : Component
            {
                protected override Component? Render() => new Div();
            }
            """);

        Assert.Contains(diagnostics, d => d.Id == "RASK014");
    }

    [Fact]
    public async Task CompleteAsync_offers_members_after_a_dot()
    {
        // Completion is tolerant of the incomplete statement; after `_count.` it should offer int members.
        const string source = """
            using Rask.Core;

            public sealed partial class Playground : Component
            {
                private int _count;

                protected override Component? Render()
                {
                    var x = _count.
                    return null;
                }
            }
            """;
        var position = source.IndexOf("_count.", StringComparison.Ordinal) + "_count.".Length;

        var completions = await NewWorkspace().CompleteAsync(source, position);

        Assert.Contains(completions, c => c.Label == "ToString");
        Assert.Contains(completions, c => c.Label == "CompareTo");
    }

    [Fact]
    public async Task CompleteAsync_offers_generated_Rask_entries_in_scope()
    {
        // `Div` resolves only because the generator emitted the chain entries into this markup host and the
        // workspace compiles the user document alongside those generated trees — the whole point of the
        // shared PlaygroundCompilation. So IntelliSense must offer `Div` at an expression position.
        const string source = """
            using Rask.Core;

            public sealed partial class Playground : Component
            {
                protected override Component? Render()
                {
                    return ;
                }
            }
            """;
        var position = source.IndexOf("return ", StringComparison.Ordinal) + "return ".Length;

        var completions = await NewWorkspace().CompleteAsync(source, position);

        Assert.Contains(completions, c => c.Label == "Div");
    }
}
