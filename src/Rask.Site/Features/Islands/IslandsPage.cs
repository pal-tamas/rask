using Rask.Core.Routing;

namespace Rask.Site.Features.Islands;

/// <summary>
///     The page for <see cref="IslandsDemo" /> — front-end components as islands in a WebAssembly app.
/// </summary>
/// <remarks>
///     Nothing in the <c>.vue</c>, <c>.tsx</c> and <c>.svelte</c> knows which host it is on: the same
///     files build unchanged on the Server host. What differs is
///     underneath — a callback reaches C# through a <c>[JSExport]</c> call into this tab's runtime
///     instead of over a WebSocket.
/// </remarks>
[Route("islands")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class IslandsPage : Component
{
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Islands demo: React, Vue, Svelte, Solid, Lit in C# — Rask",
            "Vue, React, Svelte, Solid and Lit components — and an npm React component used directly — "
            + "as ordinary C# components in a WebAssembly app.",
            Routes.IslandsPage());

    protected override Component? Render() =>
    [
        H1.Class("text-3xl font-bold mb-1")["Islands on WebAssembly"],
        P.Class("text-ui-muted")[
            "An island is an ordinary Rask component whose markup a front-end framework produces. ",
            "The same files build unchanged on the Server host — C# owns the props, the generated ",
            "types cross back into the ", Code[".vue"], ", the two ", Code[".tsx"], " and the ",
            Code[".svelte"], ", and the subtree is a diff boundary Rask never patches into. Only the ",
            "transport differs. The colour picker and its hex field have no front-end file at all: they are ",
            "react-colorful from npm, declared once as ", Code["Colorful.HexColorPicker"], " and ",
            Code["Colorful.HexColorInput"], ", their chain steps generated from the package's own TypeScript."
        ],
        P.Class("text-ui-muted")[
            "An island can also be the whole page: ",
            NavLink.Href(PageMeta.LinkTo(Routes.ReactReport())).Class("underline")["a route React owns outright"],
            ", with its title set from C# and a skeleton in the first response."
        ],
        CodeSample
            .Files([
                "IslandsDemo.cs",
                "VueChart.cs", "ChartBar.cs", "VueChart.vue",
                "ReactCounter.cs", "ReactCounter.tsx",
                "Colorful.cs", "ColorfulHexColorPicker.props.json", "ColorfulHexColorInput.props.json",
                "SvelteMeter.cs", "SvelteMeter.svelte",
                "SolidSpark.cs", "SolidSpark.tsx",
                "LitBadge.cs", "LitBadge.ts",
            ])
            .Notes("Five runtimes in one tree, running client-side, and a package component nested inside "
                + "the React island as a child. The callback that reaches C# here does so through a "
                + "[JSExport] call into this tab's own runtime; on the Server host the same front-end files "
                + "call back over the live socket.")
            .Result(IslandsDemo)
    ];
}
