using Rask.Core.Components;
using Rask.Html.Components;

namespace Rask.Example.Wasm.Jobs;

/// <summary>
///     Root of the browser-jobs sample. Returns the body's content — Rask builds the document around
///     it (RASK021) — and hosts the one demo component. Public + non-sealed to match the host's
///     ActivatorUtilities + DAM contract.
/// </summary>
public partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title["Rask — background jobs in the browser"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Style[Css]
    ];

    protected override Component? Render() =>
        Main[
            H1["Background jobs, in the browser"],
            P.Class("lede")[
                "This page queues a job into a real SQLite database running inside WebAssembly. ",
                "A ", Code["BackgroundService"], " picks it up, runs the handler, and writes a row. ",
                "Nothing here talks to a server — and the code is the same as it would be on one."
            ],
            // The generated factory, not `new` and not DI: it is what resolves JobsDemo's
            // constructor services through ActivatorUtilities and gives the framework a
            // component it owns — and therefore mounts. An instance injected into this
            // constructor would render, but never receive a lifecycle callback.
            JobsDemo
        ];

    private const string Css = """
        :root { color-scheme: light dark; }
        body {
            margin: 0;
            font: 16px/1.6 system-ui, -apple-system, "Segoe UI", sans-serif;
            background: #0f1117;
            color: #e6e8ee;
        }
        main { max-width: 44rem; margin: 0 auto; padding: 3rem 1.25rem; }
        h1 { font-size: 1.9rem; margin: 0 0 .5rem; }
        .lede { color: #9aa3b2; margin: 0 0 2rem; }
        code { background: #1b1f2a; padding: .1em .35em; border-radius: .25rem; font-size: .9em; }
        .row { display: flex; gap: .5rem; margin-bottom: 1rem; }
        input, button {
            font: inherit;
            padding: .55rem .8rem;
            border-radius: .4rem;
            border: 1px solid #2b3040;
        }
        input { flex: 1; background: #151925; color: inherit; }
        button { background: #7c3aed; color: #fff; border-color: #7c3aed; cursor: pointer; }
        button:disabled { opacity: .55; cursor: default; }
        .status { color: #9aa3b2; font-size: .9rem; min-height: 1.6em; }
        /* Amber, not red: another tab holding the database is correct behaviour, not an error. */
        .notice {
            padding: .7rem .9rem;
            margin-bottom: 1rem;
            border-radius: .4rem;
            background: #2a2213;
            border: 1px solid #5c4718;
            color: #e8d9b0;
            font-size: .92rem;
        }
        /* Green once it is actionable: the same box, but now it is good news. */
        .notice.ready {
            background: #12251a;
            border-color: #1f5c33;
            color: #b6e8c6;
        }
        ul { list-style: none; padding: 0; margin: 1rem 0 0; }
        li {
            padding: .6rem .8rem;
            background: #151925;
            border: 1px solid #222736;
            border-radius: .4rem;
            margin-bottom: .4rem;
            display: flex;
            justify-content: space-between;
            gap: 1rem;
        }
        li time { color: #6f7787; font-variant-numeric: tabular-nums; }
        .empty { color: #6f7787; font-style: italic; }
        """;
}
