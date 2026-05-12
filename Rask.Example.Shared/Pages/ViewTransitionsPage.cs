using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("view-transitions")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ViewTransitionsPage(Navigator nav) : Component
{
    private const string CssText = """
                                   .vt-hero {
                                       view-transition-name: showcase-hero;
                                       background: linear-gradient(135deg, var(--rask-accent), var(--rask-accent-strong));
                                       color: #fff;
                                       border-radius: 0.75rem;
                                       padding: 1.5rem 1.75rem;
                                       box-shadow: 0 12px 30px rgba(0, 102, 179, 0.18);
                                   }
                                   .vt-hero .vt-hero-meta { opacity: 0.75; font-size: 0.85rem; }
                                   ::view-transition-old(root),
                                   ::view-transition-new(root) {
                                       animation-duration: 320ms;
                                   }
                                   """;

    protected override string? Css => CssText;

    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "View transitions",
                "Animated route changes via the browser's View Transitions API. Built into the Rask runtime — every Navigator.Navigate(...) is wrapped in document.startViewTransition() automatically."),
            Div(Class: "vt-hero mb-4", Children:
            [
                H2(Class: "h4 fw-bold mb-2", Children: ["Try it"]),
                P(Class: "mb-3", Children:
                [
                    "Click any sidebar link, or one of the buttons below — watch for the crossfade on each navigation."
                ]),
                Div(Class: "d-flex flex-wrap gap-2", Children:
                [
                    Button(Class: "btn btn-light btn-sm",
                        OnClick: () => nav.Navigate("/binding"),
                        Children: [I(Class: "bi bi-arrow-right me-1"), "Go to /binding"]),
                    Button(Class: "btn btn-light btn-sm",
                        OnClick: () => nav.Navigate("/validation"),
                        Children: [I(Class: "bi bi-arrow-right me-1"), "Go to /validation"]),
                    Button(Class: "btn btn-outline-light btn-sm",
                        OnClick: () => nav.Navigate("/scoped-css"),
                        Children: [I(Class: "bi bi-arrow-right me-1"), "Go to /scoped-css"])
                ])
            ]),
            H2(Class: "h4 mt-5 mb-3", Children: ["How it works"]),
            P(Class: "text-secondary", Children:
            [
                "Both the server WebSocket runtime and the WASM client check each render payload for a ",
                Code(Children: ["history"]),
                " block — present only when ",
                Code(Children: ["Navigator.Navigate(...)"]),
                " produced the render. When that block is there and the browser exposes ",
                Code(Children: ["document.startViewTransition"]),
                ", the DOM morph plus the history push are wrapped in a single transition; the browser snapshots the page, swaps the DOM, snapshots the new state, and crossfades between them. State-only re-renders (counter clicks, two-way binding) skip the wrapper so event handlers stay tight."
            ]),
            H2(Class: "h4 mt-5 mb-3", Children: ["Default behaviour: no code"]),
            CodeSample(
                """
                // Just navigate — the runtime wraps the morph for you.
                Button(
                    OnClick: () => nav.Navigate("/details"),
                    Children: ["View details"])
                """,
                Notes:
                "The default ::view-transition-old(root) / ::view-transition-new(root) animation is a 250ms crossfade. Browsers without the API skip the wrap transparently."),
            H2(Class: "h4 mt-5 mb-3", Children: ["Per-element morphing"]),
            CodeSample(
                """""
                protected override string? Css => """
                    .hero {
                        view-transition-name: page-hero;
                        background: linear-gradient(135deg, #06b, #048);
                        border-radius: 0.75rem;
                        padding: 1.5rem;
                    }
                    """;

                protected override Component Render() =>
                    Div(Class: "hero", Children: ["Welcome"]);
                """"",
                Notes:
                "When the source and destination pages each render an element with the same view-transition-name, the browser morphs between them instead of crossfading the whole page.",
                Result: Div(Class: "vt-hero", Children:
                [
                    Strong(Children: ["Hero element"]),
                    Div(Class: "vt-hero-meta", Children: ["view-transition-name: showcase-hero"])
                ])),
            H2(Class: "h4 mt-5 mb-3", Children: ["Customising the animation"]),
            P(Class: "text-secondary", Children:
            [
                "Override the default with ",
                Code(Children: ["::view-transition-old(name)"]),
                " and ",
                Code(Children: ["::view-transition-new(name)"]),
                " — either in a component's scoped CSS or in the global stylesheet. To opt a single element out, set ",
                Code(Children: ["view-transition-name: none"]),
                "."
            ]),
            Div(Class: "alert alert-info d-flex align-items-start mt-4", Children:
            [
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Browser support:"]),
                    " Chromium 111+, Safari 18+, Firefox 129+. On older browsers the navigation still works — the runtime falls back to a direct ",
                    Code(Children: ["morph()"]),
                    " call."
                ])
            ])
        );
}
