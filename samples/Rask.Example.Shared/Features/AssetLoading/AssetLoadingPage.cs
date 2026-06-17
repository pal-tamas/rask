using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Showcase for the per-component content-addressed CSS/JS pipeline. Each section
///     mounts components that exercise one edge case of the new asset model:
///     <see cref="BasicScopedCss" /> (single component → single <c>&lt;link&gt;</c>),
///     <see cref="JsOnlyDemo" /> (JS-only mounted-set regression case),
///     <see cref="TwinA" /> / <see cref="TwinB" /> (two components → two distinct URLs),
///     <see cref="LazyMount" /> (mount/unmount → tag insert/remove in head morph).
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
                "Scoped CSS and JS are delivered as ",
                Strong()["per-component, content-addressed"],
                " assets at ",
                Code()["/_rask/a/{hash}.{ext}"],
                " with ",
                Code()["Cache-Control: public, max-age=31536000, immutable"],
                ". Open DevTools → Network and observe each section below."
            ],
            H2(Class: "h5 fw-semibold mb-2 mt-5")["Basic scoped CSS"],
            CodeSample(
                ["BasicScopedCss.cs", "BasicScopedCss.css"],
                Notes:
                "One component with a sibling .css file. Exactly one <link> request — the framework " +
                "hashes the rewritten CSS and emits a single content-addressed tag into <head>.",
                Result: BasicScopedCss()),
            H2(Class: "h5 fw-semibold mb-2 mt-5")["JS-only component"],
            CodeSample(
                ["JsOnlyDemo.cs", "JsOnlyDemo.js"],
                Notes:
                "Sibling .js, no .css. Used to regress when the mounted-set only tracked CSS components. " +
                "Click the button — it dispatches via IJSRuntime to the scoped JS module.",
                Result: JsOnlyDemo()),
            H2(Class: "h5 fw-semibold mb-2 mt-5")["Two components, two URLs"],
            CodeSample(
                ["TwinA.cs", "TwinA.css"],
                Notes:
                "Different rewritten content → different content hash → two independent <link>s. " +
                "Either component edited in isolation only re-fetches its own bytes. TwinB is the same " +
                "shape with its own .css, so it gets a distinct hash.",
                Result: Div(Class: "d-flex gap-2 flex-wrap")[TwinA(), TwinB()]),
            H2(Class: "h5 fw-semibold mb-2 mt-5")["Lazy mount / unmount"],
            CodeSample(
                ["LazyMount.cs", "LazyChild.cs", "LazyChild.css"],
                Notes:
                "Toggle the button. On mount, the framework adds the child's <link> via the head morph; " +
                "on unmount, the tag is removed. The browser keeps the CSS bytes cached, so re-mounting is instant.",
                Result: LazyMount()),
            Div(Class: "alert alert-info d-flex align-items-start mt-5")[
                I(Class: "bi bi-info-circle-fill me-3 fs-4"),
                Div()[
                    Strong()["Verify in DevTools:"],
                    Ul(Class: "mb-0")[
                        Li()["each <link> targets ", Code()["/_rask/a/{12-hex}.css"]],
                        Li()["response headers include ", Code()["cache-control: public, max-age=31536000, immutable"]],
                        Li()[
                            "a ", Code()["data-rask-key=\"rsk-css-{hash}\""],
                            " is on every tag (used by the client morph to reconcile by identity, not position)"
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
