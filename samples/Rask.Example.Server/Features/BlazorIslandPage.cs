using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Server.Features;

/// <summary>
///     Server-host showcase page for <see cref="BlazorIslandDemo" /> — a real <c>.razor</c> from a
///     referenced Razor Class Library, hosted as an ordinary Rask component and driven over the live
///     WebSocket.
/// </summary>
/// <remarks>
///     The WASM showcase gates the trimming half of <c>Rask.Blazor</c>. This page gates the half only
///     the Server host has: a hosted component that calls a browser API gets its answer back over the
///     socket, which means the call escalated the page to a live session, the injected
///     <c>IJSRuntime</c> was real, and Rask fired an after-render hook that
///     <c>StaticHtmlRenderer</c> never fires.
///
///     Every one of those was broken or absent before #956, and no test in the lane could see it —
///     the unit tests use a fake runtime and the only sample was WASM.
/// </remarks>
[Route("blazor-island")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class BlazorIslandPage : Component
{
    protected override Component? HeadAssets => Title["Blazor island — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Blazor island"],
        P.Class("text-slate-500 dark:text-slate-400")[
            "A real Blazor component — compiled by the Razor SDK, in a class library that knows ",
            "nothing about Rask — rendered inside this server-rendered page. There is no circuit and ",
            "no blazor.web.js: the component's own @onclick and @bind travel the same handler channel ",
            "every Rask event uses, over the socket that is already open."
        ],
        CodeSample
            .Files(["BlazorIslandDemo.cs"])
            .Notes("Deriving from BlazorComponent<T> is the whole declaration — the chain steps are read "
                + "from the .razor's own [Parameter] properties.")
            .Result(BlazorIslandDemo)
    ];
}
