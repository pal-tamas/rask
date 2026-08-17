using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Playground.Compiler;

namespace Rask.Example.Playground.Tests;

// The README's "Markup is a chain" section is the first Rask code most people ever read, and nothing
// compiled it: the tutorial snippet gate only walks docs/tutorial, and prose does not build. So the exact
// chains it teaches are compiled here, through the same in-process Roslyn + generator pipeline the
// playground uses. A step that gets renamed or dropped fails this test instead of greeting a newcomer.
//
// Keep these snippets character-identical to the README block they mirror; the point is to prove THAT
// text, not a paraphrase of it.
public sealed class ReadmeChainSnippetTests
{
    private static PlaygroundCompiler NewCompiler() =>
        new(TestReferences.Build(), new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Readme_markup_chain_example_compiles()
    {
        var result = await NewCompiler().CompileAsync("""
            using System;
            using Rask.Core;

            namespace Demo;

            public sealed partial class Playground : Component
            {
                private void Save() { }

                protected override Component? Render() =>
                    Div.Class("card")[
                        H2.Class("card-title")["Products"],
                        Button.Class("btn").OnClick(Save)["Save"]
                    ];
            }
            """);

        Assert.True(result.Succeeded, Dump(result));
        Assert.Contains("card-title", result.Component!.ToHtml());
    }

    [Fact]
    public async Task Readme_bound_and_controlled_input_examples_compile()
    {
        var result = await NewCompiler().CompileAsync("""
            using System;
            using Rask.Core;
            using Rask.Core.Forms;

            namespace Demo;

            public sealed partial class Playground : Component
            {
                private readonly Model _form = new();
                private string _text = "";

                protected override Component? Render() =>
                    Form.Model(_form)[
                        Input.Bind(() => _form.Name).Placeholder("Ada Lovelace"),
                        Input.Value(_text).OnChange(v => _text = v)
                    ];

                private sealed class Model
                {
                    public string Name { get; set; } = "";
                }
            }
            """);

        Assert.True(result.Succeeded, Dump(result));
    }

    [Fact]
    public async Task Readme_own_component_example_compiles()
    {
        var result = await NewCompiler().CompileAsync("""
            using System;
            using Rask.Core;

            namespace Demo;

            public sealed partial class ProductCard : Component
            {
                public required string Title { get; set; }   // a step — the chain asks for it first
                public string? Subtitle { get; set; }        // a setter
                public Action? OnPick { get; set; }          // a plain delegate; call it with OnPick?.Invoke()

                protected override Component? Render() =>
                    Div[Span[Title], Span[Subtitle ?? ""]];
            }

            public sealed partial class Playground : Component
            {
                private void Pick() { }

                protected override Component? Render() =>
                    ProductCard.Title("Coffee").Subtitle("Dark roast").OnPick(Pick);
            }
            """);

        Assert.True(result.Succeeded, Dump(result));
        Assert.Contains("Coffee", result.Component!.ToHtml());
    }

    // docs/building-components.md is where the README sends a reader to learn the chain properly, so its
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

    private static string Dump(PlaygroundResult result) =>
        "Diagnostics:\n" + string.Join("\n",
            result.Diagnostics.Select(d => $"  {d.Severity} {d.Id} ({d.StartLine},{d.StartColumn}): {d.Message}"));
}
