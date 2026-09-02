using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     Showcase page for <see cref="IslandsDemo" /> — a Vue and a Svelte component used as ordinary
///     Rask components.
/// </summary>
/// <remarks>
///     On the Server host because these islands need npm, and this sample is the one that carries a
///     <c>package.json</c>. Nothing about the components is host-specific: the same files build and
///     mount on WASM, where a callback reaches C# through a <c>[JSExport]</c> call into this tab's
///     runtime instead of over a socket.
/// </remarks>
[Route("islands")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class IslandsPage : Component
{
    protected override Component? HeadAssets => Title["Islands — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Islands — Vue and Svelte"],
        P.Class("text-slate-500 dark:text-slate-400")[
            "An island is an ordinary Rask component whose markup is produced by a front-end framework. ",
            "It goes anywhere the chain goes — a leaf inside a card, a subtree, or the whole of a page's ",
            Code["Render()"], ". C# owns the props, the generated types cross back into the ", Code[".vue"],
            " and ", Code[".svelte"], " files, and the subtree is a diff boundary Rask never patches into."
        ],
        CodeSample
            .Files([
                "IslandsDemo.cs",
                "VueChart.cs", "VueChart.vue",
                "ReactCounter.cs", "ReactCounter.tsx",
                "LitBadge.cs", "LitBadge.ts",
                "SvelteMeter.cs", "SvelteMeter.svelte",
            ])
            .Notes("Four runtimes on one page, in one tree — React, Vue, Svelte and Lit, with Preact "
                + "riding the React adapter unchanged. The Vue chart's callback re-enters C# over the "
                + "live WebSocket; the React counter and the Svelte meter keep local state C# never "
                + "sees, which is what shows a prop change reconciles rather than remounting.")
            .Result(IslandsDemo)
    ];
}
