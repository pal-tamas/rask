using Rask.Core.Routing;
using Rask.Site;

namespace Rask.Site.Features.Islands;

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
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Islands demo: React, Vue, Svelte and Lit in C# — Rask",
            "Vue, React, Svelte, Solid and Lit components — and an npm React component used directly — "
            + "as ordinary C# components in a WebAssembly app.",
            Routes.IslandsPage());

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Islands on WebAssembly"],
        P.Class("text-ui-muted")[
            "An island is an ordinary Rask component whose markup a front-end framework produces. ",
            "These are the same files the Server showcase builds — C# owns the props, the generated ",
            "types cross back into the ", Code[".vue"], ", the two ", Code[".tsx"], " and the ",
            Code[".svelte"], ", and the subtree is a diff boundary Rask never patches into. Only the ",
            "transport differs. The colour picker has no front-end file at all: it is react-colorful ",
            "from npm, its chain steps generated from the package's own TypeScript into ",
            Code["ColorPicker.props.json"], "."
        ],
        CodeSample
            .Files([
                "IslandsDemo.cs",
                "VueChart.cs", "VueChart.vue",
                "ReactCounter.cs", "ReactCounter.tsx",
                "ColorPicker.cs", "ColorPicker.props.json",
                "SvelteMeter.cs", "SvelteMeter.svelte",
                "SolidSpark.cs", "SolidSpark.tsx",
            ])
            .Notes("Four runtimes in one tree, running client-side, and a package component nested inside "
                + "the React island as a child. The callback that reaches C# here does so through a "
                + "[JSExport] call into this tab's own runtime; the front-end files are byte-identical to "
                + "the Server showcase's.")
            .Result(IslandsDemo)
    ];
}
