using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

// The first Rask code most people ever read is the README's, and the second is
// docs/building-components.md's. Neither compiles on its own: the tutorial snippet gate only walks
// docs/tutorial, and prose does not build. So the exact chains they teach are compiled here, through the
// real ComponentFactoryGenerator with the builder surface on. A step that gets renamed or dropped fails
// this test instead of greeting a newcomer.
//
// These snippets used to live in tests/Rask.Example.Playground.Tests/ChainSnippetTests.cs and ran through
// the playground's in-browser compiler. The playground is gone; the gate is not, because
// scripts/tests/front-doors.test.sh leans on it — it proves the README hero and the site hero are the same
// text, and this proves that text compiles. Without both halves the front page can drift to an API that no
// longer exists and stay green.
//
// What did NOT survive the move: two of these cases also rendered the component and asserted on its HTML.
// That needed a compiler that emits and executes, which the playground had and this harness does not.
// The compile check is the half that caught the two live defects recorded below, so it is the half kept.
//
// Keep these snippets character-identical to the block each mirrors; the point is to prove THAT text, not
// a paraphrase of it. Each test names its source file in a comment — when the prose moves, move the mirror.
public sealed class DocSnippetTests
{
    // README.md — the only C# on the front page. It is a Page, so it also pins what a routable component
    // needs: a [Route] naming the URL it answers, and a parameterless render. This one returns a SINGLE
    // root rather than a collection expression, which is the case that proves a bare chain converts to
    // Component? on the way out — see Build<T> in CLAUDE.md.
    [Fact]
    public void Readme_counter_example_compiles() => AssertCompiles("""
        using System;
        using Rask.Core;
        using Rask.Core.Routing;

        namespace Demo;

        [Route("/counter")]
        public sealed partial class Counter : Component
        {
            private int _count;

            protected override Component? Render() =>
                Button.OnClick(() => _count++)[$"Current count: {_count}"];
        }
        """);

    // docs/building-components.md — where the README sends a reader to learn the chain properly, so its
    // Rask.Core examples are held to the same bar. (Its Bs* examples need Rask.Bootstrap, which is not in
    // this reference set — those stay uncovered here.) This caught two live defects when it was written:
    // a trailing comma inside the [ … ] indexer, which is an argument list and does not take one, and a
    // `.Change(…)` step that has never existed — the property is `OnChange`.
    [Fact]
    public void Building_components_doc_core_examples_compile() => AssertCompiles("""
        using System;
        using System.Collections.Generic;
        using Rask.Core;
        using Rask.Core.Forms;

        namespace Demo;

        public sealed partial class Host : Component
        {
            private readonly Model _form = new();
            private string _text = "";

            protected override Component? Render() =>
                Div.Class("panel")[
                    H2.Class("panel-title")["Products"],
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

    // docs/building-components.md, "Your own components" — the claim that a component you write gets the
    // identical surface. The doc elides the render body as `…`; everything else is verbatim, and the
    // required/optional/delegate split is the whole point of the example.
    [Fact]
    public void Building_components_doc_own_component_example_compiles() => AssertCompiles("""
        using System;
        using Rask.Core;

        namespace Demo;

        public sealed partial class ProductCard : Component
        {
            public required string Title { get; set; }   // a step
            public string? Subtitle { get; set; }        // a setter
            public Action? OnPick { get; set; }

            protected override Component? Render() =>
                Div.Class("panel")[
                    H2[Title],
                    P[Subtitle ?? ""],
                    Button.OnClick(() => OnPick?.Invoke())["Pick"]
                ];
        }

        public sealed partial class Host : Component
        {
            private void Pick() { }

            protected override Component? Render() =>
                ProductCard.Title("Coffee").Subtitle("Dark roast").OnPick(Pick);
        }
        """);

    // docs/forms.md §2 — the first form anyone reads. It taught `Form<SignupModel>(_model,
    // OnValidSubmit: …)`, a factory call dropped in #792, sitting a few lines above the chain spelling
    // it had been replaced by: one document, two syntaxes, one of which does not compile (#1007). Prose
    // does not build, so the corrected text is compiled here.
    [Fact]
    public void Forms_doc_form_and_context_examples_compile() => AssertCompiles("""
        using System;
        using Rask.Core;
        using Rask.Core.Forms;

        namespace Demo;

        public sealed class SignupModel
        {
            public string Username { get; set; } = "";
        }

        public sealed partial class Host : Component
        {
            private readonly SignupModel _model = new();

            protected override Component? Render() =>
                Form.Model(_model).OnValidSubmit(m => Console.WriteLine(m.Username))[
                    Input.Bind(() => _model.Username),
                    Button.Type("submit")["Sign up"]
                ];
        }
        """);

    // The same doc's "Auto-created vs explicit Context" example, which carried the factory's
    // `Context:` argument. `Context` is a chain step, and it composes with OnValidSubmit rather than
    // replacing it — which is exactly what the broken spelling obscured.
    [Fact]
    public void Forms_doc_explicit_context_example_compiles() => AssertCompiles("""
        using System;
        using Rask.Core;
        using Rask.Core.Forms;

        namespace Demo;

        public sealed class TaskModel
        {
            public string Title { get; set; } = "";
        }

        public sealed partial class Host : Component
        {
            private readonly TaskModel _model = new();
            private EditContext? _ctx;
            private string? _submission;

            protected override Component? Render()
            {
                _ctx ??= new EditContext(_model);

                return Form.Model(_model).OnValidSubmit(m => _submission = "Saved").Context(_ctx)[
                    Input.Bind(() => _model.Title),
                    Button.Type("submit").Disabled(_ctx.IsValidatingAny)["Save"]
                ];
            }
        }
        """);

    // Errors only. Warnings are noise here — an unused private field in a snippet that mirrors prose is
    // the prose being an excerpt, not the API being wrong.
    private static void AssertCompiles(string source)
    {
        var errors = BuilderGeneratorHarness.Compile(source)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            "The snippet did not compile:\n" + string.Join(
                "\n", errors.Select(d => $"  {d.Id} {d.Location.GetLineSpan().StartLinePosition}: {d.GetMessage()}")));
    }
}
