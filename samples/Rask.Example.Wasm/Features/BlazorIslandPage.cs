using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     WASM-only showcase page for <see cref="BlazorIslandDemo" /> — a real <c>.razor</c> from a
///     referenced Razor Class Library, hosted as an ordinary Rask component in a browser-WASM app.
/// </summary>
/// <remarks>
///     Here rather than in <c>Rask.Example.Shared</c> deliberately: this page is what makes the
///     browser-WASM half of <c>Rask.Blazor</c> a GATED path rather than a claim. The showcase publishes
///     trimmed, and a hosted component's parameters are assigned by reflection inside
///     <c>Microsoft.AspNetCore.Components</c> — so without the annotation on
///     <c>BlazorComponent&lt;TComponent&gt;</c> the trimmer removes the setters and the island renders
///     EMPTY, with no build warning and no console error. The E2E over this page is what says so.
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
            "A real Blazor component — compiled by the Razor SDK, in a referenced class library that ",
            "knows nothing about Rask — rendered inside this page, in the browser, on WebAssembly. ",
            "Deriving from BlazorComponent<T> is the whole declaration: the chain steps are read from ",
            "the component's own [Parameter] properties, so Symbol is required because the .razor ",
            "marked it [EditorRequired]."
        ],
        CodeSample
            .Files(["BlazorIslandDemo.cs", "PriceTicker.razor"])
            .Notes("Watch and the note field are the HOSTED component's own @onclick and @bind. There is "
                + "no Blazor circuit and no blazor.web.js: they travel the same handler channel every "
                + "Rask event uses, which on this host is a JSExport call into this tab's runtime.")
            .Result(BlazorIslandDemo)
    ];
}
