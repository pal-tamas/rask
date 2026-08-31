namespace Rask.Example.Playground;

/// <summary>One entry in the left-hand example gallery: a titled, one-line-blurbed, ready-to-run snippet.</summary>
/// <remarks>
///     Code is kept verbatim (raw strings) so what the visitor sees is exactly what compiles. Each snippet
///     defines a component named <c>Playground</c> as the entry point, lives in a namespace (as a real Rask
///     project does), declares its components <c>partial</c> so the generator can inject the builder entries
///     into them, and uses only types in the shipped <c>_framework</c> (BCL + Rask.Core). Note the explicit
///     <c>using</c>s: the in-browser compile has no MSBuild implicit usings, so a snippet must bring in
///     <c>System.Collections.Generic</c> / <c>System.Linq</c> itself.
/// </remarks>
public sealed record PlaygroundSample(string Id, string Title, string Blurb, string Code);

/// <summary>The curated examples shown in the playground's gallery. The first is the default on load.</summary>
public static class PlaygroundSamples
{
    public static readonly IReadOnlyList<PlaygroundSample> All = new[]
    {
        new PlaygroundSample(
            "counter",
            "Counter",
            "State in a field, a click handler, automatic re-render.",
            """
            using Rask.Core;

            namespace Demo;

            // Welcome to the Rask playground! This C# is compiled in your browser — Roslyn and the Rask
            // source generator run in WebAssembly, no server involved. Edit the code, then press Run
            // (or Ctrl/Cmd + Enter). Define a component named `Playground` as the entry point.
            //
            // Markup is a chain: name a component, then dot onto it. `Div` IS a div, so pressing `.`
            // lists everything it has. Children go in the [] indexer. There is no `new`, no factory
            // call, and nothing to import.
            public sealed partial class Playground : Component
            {
                private int _count;

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Hello, Rask 👋"],
                        P[$"You clicked {_count} times."],
                        Button.Class("action").OnClick(() => _count++)["Click me"]
                    ];
            }
            """),

        new PlaygroundSample(
            "form",
            "Form + validation",
            "Built-in Form<T> validation: per-field + form-level Validate rules, live as you type.",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Rask.Core;
            using Rask.Core.Forms;

            namespace Demo;

            // Rask's built-in form validation. Form runs per-field `.Validate(…)` rules and a form-level
            // one; ValidationMessage renders a field's errors and ValidationSummary the form-level ones.
            // Inputs are two-way bound with Input.Bind(() => model.Field) — everything re-checks as you type.
            //
            // Notice how the chain reads: `.Bind(…)` comes first because an input cannot exist until it
            // knows what it binds to. Once it does, everything optional follows in any order.
            public sealed partial class Playground : Component
            {
                private readonly SignUp _model = new();
                private string? _welcome;

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Create account"],
                        Form.Model(_model)
                            .OnValidSubmit(m => _welcome = $"Welcome, {m.Name}!")
                            .Validate(m => m.Password == m.Confirm
                                ? Array.Empty<string>()
                                : new[] { "Passwords do not match." })[

                            Label["Name"],
                            Input.Bind(() => _model.Name)
                                .Class("field")
                                .Placeholder("Ada Lovelace")
                                .Validate(v => v.Trim().Length > 0
                                    ? Array.Empty<string>()
                                    : new[] { "Name is required." }),
                            ValidationMessage.Template(Errors).For(() => _model.Name),

                            Label["Email"],
                            Input.Bind(() => _model.Email)
                                .Type(InputType.Email)
                                .Class("field")
                                .Placeholder("ada@example.com")
                                .Validate(v => v.Contains('@')
                                    ? Array.Empty<string>()
                                    : new[] { "Enter a valid email address." }),
                            ValidationMessage.Template(Errors).For(() => _model.Email),

                            Label["Password"],
                            Input.Bind(() => _model.Password).Type(InputType.Password).Class("field"),

                            Label["Confirm password"],
                            Input.Bind(() => _model.Confirm).Type(InputType.Password).Class("field"),

                            ValidationSummary.Template(Summary),
                            Button.Type("submit").Class("action")["Sign up"]
                        ],
                        _welcome is null ? null : P.Class("ok")[_welcome]
                    ];

                // Render a field's error messages (called by ValidationMessage).
                private static Component Errors(IReadOnlyList<string> messages) =>
                    [.. messages.Select((m, i) => P.Key(i).Class("error")[m])];

                // Render only the form-level errors (fields render their own above).
                private static Component? Summary(IReadOnlyList<ValidationEntry> entries)
                {
                    var formOnly = entries.Where(e => e.Field.Length == 0).ToList();
                    return formOnly.Count == 0
                        ? null
                        : Ul.Class("errors")[formOnly.Select((e, i) => Li.Key(i)[e.Message])];
                }

                private sealed class SignUp
                {
                    public string Name { get; set; } = "";
                    public string Email { get; set; } = "";
                    public string Password { get; set; } = "";
                    public string Confirm { get; set; } = "";
                }
            }
            """),

        new PlaygroundSample(
            "todo",
            "Todo app",
            "A bound text field, a keyed list, add / toggle / remove — plain C# state.",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Rask.Core;

            namespace Demo;

            // A small todo app: a two-way-bound text field, a keyed list (.Key(…) keeps identity across
            // edits), and add / toggle / remove over ordinary C# state — all re-rendered automatically.
            public sealed partial class Playground : Component
            {
                private readonly List<Todo> _todos = new()
                {
                    new Todo("Try the Rask playground", true),
                    new Todo("Star the repo on GitHub", false),
                };
                private readonly Draft _draft = new();

                protected override Component? Render() =>
                    Div.Class("panel")[
                        H1["Todos"],
                        Div.Class("line")[
                            Input.Bind(() => _draft.Text).Class("field").Placeholder("What needs doing?"),
                            Button.Class("action").OnClick(Add)["Add"]
                        ],
                        Ul.Class("list")[
                            _todos.Select(t => Li.Key(t.Id).Class(t.Done ? "done" : null)[
                                Label.Class("item")[
                                    Input.Bind(() => t.Done),
                                    Span[t.Text]
                                ],
                                Button.Class("link").OnClick(() => _todos.Remove(t))["Remove"]
                            ])
                        ],
                        P.Class("muted")[$"{_todos.Count(t => !t.Done)} of {_todos.Count} remaining"]
                    ];

                private void Add()
                {
                    var text = _draft.Text.Trim();
                    if (text.Length > 0)
                    {
                        _todos.Add(new Todo(text, false));
                        _draft.Text = "";
                    }
                }

                private sealed class Todo
                {
                    public Todo(string text, bool done) { Text = text; Done = done; }
                    public Guid Id { get; } = Guid.NewGuid();
                    public string Text { get; set; }
                    public bool Done { get; set; }
                }

                private sealed class Draft
                {
                    public string Text { get; set; } = "";
                }
            }
            """),
    };

    /// <summary>The snippet the editor opens with (the first gallery entry).</summary>
    public static string Starter => All[0].Code;
}
