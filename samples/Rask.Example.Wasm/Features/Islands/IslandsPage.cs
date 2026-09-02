using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features.Islands;

/// <summary>
///     WASM showcase page for <see cref="IslandsDemo" /> — the same islands the Server host runs,
///     from byte-identical front-end files.
/// </summary>
/// <remarks>
///     This page is the point of the pair: the <c>.vue</c>, <c>.tsx</c> and <c>.svelte</c> are copies
///     of the Server showcase's, and nothing in them knows which host they are on. What differs is
///     underneath — a callback reaches C# through a <c>[JSExport]</c> call into this tab's runtime
///     instead of over a WebSocket.
/// </remarks>
[Route("islands")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class IslandsPage : Component
{
    protected override Component? HeadAssets => Title["Islands — Rask"];

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Islands on WebAssembly"],
        P.Class("text-slate-500 dark:text-slate-400")[
            "An island is an ordinary Rask component whose markup a front-end framework produces. ",
            "These are the same files the Server showcase builds — C# owns the props, the generated ",
            "types cross back into the ", Code[".vue"], ", ", Code[".tsx"], " and ", Code[".svelte"],
            ", and the subtree is a diff boundary Rask never patches into. Only the transport differs."
        ],
        CodeSample
            .Files([
                "IslandsDemo.cs",
                "VueChart.cs", "VueChart.vue",
                "ReactCounter.cs", "ReactCounter.tsx",
                "SvelteMeter.cs", "SvelteMeter.svelte",
            ])
            .Notes("Three runtimes in one tree, running client-side. The callback that reaches C# here "
                + "does so through a [JSExport] call into this tab's own runtime; the front-end files "
                + "are byte-identical to the Server showcase's.")
            .Result(IslandsDemo)
    ];
}
