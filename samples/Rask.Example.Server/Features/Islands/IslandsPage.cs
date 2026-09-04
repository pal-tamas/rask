using Rask.Core.Routing;
using Rask.Example.Shared;

namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     Showcase page for <see cref="IslandsDemo" /> — six front-end runtimes used as ordinary Rask
///     components.
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
        H1.Class("text-3xl font-bold mb-1")["Islands — six runtimes, one tree"],
        P.Class("text-ui-muted")[
            "An island is an ordinary Rask component whose markup is produced by a front-end framework. ",
            "It goes anywhere the chain goes — a leaf inside a card, a subtree, or the whole of a page's ",
            Code["Render()"], ". C# owns the props, the generated types cross back into the ", Code[".tsx"],
            ", ", Code[".vue"], " and ", Code[".svelte"], " files, and the subtree is a diff boundary Rask ",
            "never patches into."
        ],
        CodeSample
            .Files([
                "IslandsDemo.cs",
                "VueChart.cs", "VueChart.vue",
                "ReactCounter.cs", "ReactCounter.tsx",
                "SolidSpark.cs", "SolidSpark.tsx",
                "AngularTicker.cs", "AngularTicker.ts",
                "LitBadge.cs", "LitBadge.ts",
                "SvelteMeter.cs", "SvelteMeter.svelte",
            ])
            .Notes("Six runtimes on one page, in one tree — React, Solid, Vue, Svelte, Angular and "
                + "Lit. Preact is the seventh and the one that cannot join them here: its Vite plugin "
                + "and React's pin different majors of Babel, so npm refuses to install both. Which "
                + "runtime an island uses is decided by its BASE CLASS, never by its extension — "
                + "React and Solid both write .tsx, Angular and Lit both write .ts. React and Solid "
                + "sit in folders of their own because two JSX plugins scoped to one directory would "
                + "each claim the other's files. The Vue chart's callback re-enters C# over the live "
                + "WebSocket; every component keeps local state C# never sees, which is what shows a "
                + "prop change reconciles rather than remounting.")
            .Result(IslandsDemo)
    ];
}
