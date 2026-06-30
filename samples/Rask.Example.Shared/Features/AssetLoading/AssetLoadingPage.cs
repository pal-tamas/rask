using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Showcase for the scoped CSS/JS bundle pipeline. You author a sibling <c>.css</c>/<c>.js</c>
///     per component; the framework concatenates every component's scoped CSS into one
///     content-addressed bundle and every scoped JS into another. Each section mounts components
///     that contribute to those two bundles: <see cref="BasicScopedCss" /> (a sibling <c>.css</c>),
///     <see cref="JsOnlyDemo" /> (a sibling <c>.js</c>), <see cref="TwinA" /> / <see cref="TwinB" />
///     (two components, one shared bundle), <see cref="LazyMount" /> (a later mount is styled
///     instantly — its rules already ride the bundle).
/// </summary>
[Route("/asset-loading")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class AssetLoadingPage : Component
{
    protected override RenderResult Head => Title()["Asset loading — Rask"];

    protected override RenderResult Render() =>
        Div(Class: "container py-4")[
            H1(Class: "h3 mb-3")["Asset loading"],
            P(Class: "text-secondary mb-4")[
                "Scoped CSS and JS each ship as ",
                Strong()["one content-addressed bundle"],
                ", served at ",
                Code()["/_rask/a/{hash}.{ext}"],
                " with ",
                Code()["Cache-Control: public, max-age=31536000, immutable"],
                ". The page emits exactly one ", Code()["<link>"], " and one ", Code()["<script defer>"],
                ". Open DevTools → Network and observe each section below."
            ],
            H2(Class: "h5 fw-semibold mb-2 mt-5")["Basic scoped CSS"],
            CodeSample(
                ["BasicScopedCss.cs", "BasicScopedCss.css"],
                Notes:
                "A component with a sibling .css file. Its rules are selector-rewritten to its scope id " +
                "and concatenated into the single CSS bundle — author per-component, ship as one file.",
                Result: BasicScopedCss()),
            H2(Class: "h5 fw-semibold mb-2 mt-5")["JS-only component"],
            CodeSample(
                ["JsOnlyDemo.cs", "JsOnlyDemo.js"],
                Notes:
                "Sibling .js, no .css. Its module joins the single JS bundle (exposed as window.Rask[...]). " +
                "Click the button — it dispatches via IJSRuntime to the scoped JS module.",
                Result: JsOnlyDemo()),
            H2(Class: "h5 fw-semibold mb-2 mt-5")["Two components, one bundle"],
            CodeSample(
                ["TwinA.cs", "TwinA.css"],
                Notes:
                "Two components, each with its own scoped .css — both concatenate into the same bundle. " +
                "The bundle's content hash (and therefore its immutable URL) changes only when the combined " +
                "scoped CSS changes. TwinB is the same shape with its own .css.",
                Result: Div(Class: "d-flex gap-2 flex-wrap")[TwinA(), TwinB()]),
            H2(Class: "h5 fw-semibold mb-2 mt-5")["Lazy mount / unmount"],
            CodeSample(
                ["LazyMount.cs", "LazyChild.cs", "LazyChild.css"],
                Notes:
                "Toggle the button. The child's scoped CSS already rides the bundle shipped at page load, " +
                "so it is styled the instant it mounts — no extra request and no flash of unstyled content.",
                Result: LazyMount()),
            BsAlert(Color: BsColor.Info, Class: "d-flex align-items-start mt-5")[
                BsIcon(Name: BsIconName.InfoCircleFill, Class: "me-3 fs-4"),
                Div()[
                    Strong()["Verify in DevTools:"],
                    Ul(Class: "mb-0")[
                        Li()["one ", Code()["<link>"], " and one ", Code()["<script>"], " target ",
                            Code()["/_rask/a/{12-hex}.{ext}"]],
                        Li()["response headers include ", Code()["cache-control: public, max-age=31536000, immutable"]],
                        Li()[
                            "the bundle tags carry the stable ", Code()["data-rask-key=\"rsk-css\""],
                            " / ", Code()["rsk-js"],
                            " (the client morph reconciles by identity, not position)"
                        ],
                        Li()[
                            "method ", Code()["HEAD /_rask/a/{hash}.css"],
                            " returns the same headers with an empty body — try it from the Network tab"
                        ]
                    ]
                ]
            ]
        ];
}
