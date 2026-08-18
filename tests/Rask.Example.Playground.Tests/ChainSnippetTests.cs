using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Playground.Compiler;

namespace Rask.Example.Playground.Tests;

// The first Rask code most people ever read is the README's, and the second is
// docs/building-components.md's. Neither compiled: the tutorial snippet gate only walks docs/tutorial, and
// prose does not build. So the exact chains they teach are compiled here, through the same in-process
// Roslyn + generator pipeline the playground uses. A step that gets renamed or dropped fails this test
// instead of greeting a newcomer.
//
// Keep these snippets character-identical to the block each mirrors; the point is to prove THAT text, not a
// paraphrase of it. Each test names its source file in a comment — when the prose moves, move the mirror.
public sealed class ChainSnippetTests
{
    private static PlaygroundCompiler NewCompiler() =>
        new(TestReferences.Build(), new ServiceCollection().BuildServiceProvider());

    // README.md — the only C# on the front page. It is a Page, so it also pins the two things a routable
    // component needs: a [Route] naming the URL it answers, and a parameterless render that returns a
    // collection expression rather than a single root.
    [Fact]
    public async Task Readme_counter_example_compiles()
    {
        var result = await NewCompiler().CompileAsync("""
            using System;
            using Rask.Core;
            using Rask.Core.Routing;

            namespace Demo;

            [Route("/counter")]
            public sealed partial class Counter : Component
            {
                private int _count;

                protected override Component? Render() =>
                [
                    H1["Counter"],
                    P[$"Current count: {_count}"],
                    Button.OnClick(() => _count++)["Click me"]
                ];
            }
            """);

        Assert.True(result.Succeeded, Dump(result));

        var html = result.Component!.ToHtml();
        Assert.Contains("Current count: 0", html, StringComparison.Ordinal);
        Assert.Contains("Click me", html, StringComparison.Ordinal);
    }

    // docs/building-components.md — where the README sends a reader to learn the chain properly, so its
    // Rask.Core examples are held to the same bar. (Its Bs* examples need Rask.Bootstrap, which is not in
    // this reference set — those stay uncovered here.) This caught two live defects when it was written:
    // a trailing comma inside the [ … ] indexer, which is an argument list and does not take one, and a
    // `.Change(…)` step that has never existed — the property is `OnChange`.
    [Fact]
    public async Task Building_components_doc_core_examples_compile()
    {
        var result = await NewCompiler().CompileAsync("""
            using System;
            using System.Collections.Generic;
            using Rask.Core;
            using Rask.Core.Forms;

            namespace Demo;

            public sealed partial class Playground : Component
            {
                private readonly Model _form = new();
                private string _text = "";

                protected override Component? Render() =>
                    Div.Class("card")[
                        H2.Class("card-title")["Products"],
                        P["Everything we sell."]
                    ];

                private Component Bound() =>
                    Input.Bind(() => _form.Name).Validate(Check).Id("name");

                private Component Controlled() =>
                    Input.Value(_text).OnChange(v => _text = v);

                // Where the value alone names no type, say it once.
                private Component Untyped() =>
                    Input.Value<string>(null).Placeholder("Anything");

                private static IEnumerable<string> Check(string value) => Array.Empty<string>();

                private sealed class Model
                {
                    public string Name { get; set; } = "";
                }
            }
            """);

        Assert.True(result.Succeeded, Dump(result));
    }

    // docs/building-components.md, "Your own components" — the claim that a component you write gets the
    // identical surface. The doc elides the render body as `…`; everything else is verbatim, and the
    // required/optional/delegate split is the whole point of the example.
    [Fact]
    public async Task Building_components_doc_own_component_example_compiles()
    {
        var result = await NewCompiler().CompileAsync("""
            using System;
            using Rask.Core;

            namespace Demo;

            public sealed partial class ProductCard : Component
            {
                public required string Title { get; set; }   // a step
                public string? Subtitle { get; set; }        // a setter
                public Action? OnPick { get; set; }

                protected override Component? Render() =>
                    Div.Class("card")[
                        H2[Title],
                        P[Subtitle ?? ""],
                        Button.OnClick(() => OnPick?.Invoke())["Pick"]
                    ];
            }

            // Named Playground because the compiler needs one entry component by that name when a snippet
            // declares more than one; the call site below is what the doc actually shows.
            public sealed partial class Playground : Component
            {
                private void Pick() { }

                protected override Component? Render() =>
                    ProductCard.Title("Coffee").Subtitle("Dark roast").OnPick(Pick);
            }
            """);

        Assert.True(result.Succeeded, Dump(result));
        Assert.Contains("Coffee", result.Component!.ToHtml(), StringComparison.Ordinal);
    }

    private static string Dump(PlaygroundResult result) =>
        "Diagnostics:\n" + string.Join("\n",
            result.Diagnostics.Select(d => $"  {d.Severity} {d.Id} ({d.StartLine},{d.StartColumn}): {d.Message}"));
}
