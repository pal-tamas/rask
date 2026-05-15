namespace Rask.Example.Shared;

public sealed class App : Component
{
    // Override Bootstrap's primary palette with Rask's brand blue (#0066B3 — a nod to
    // the Norwegian/Danish/Swedish origin of "rask" = fast). The --rask-accent /
    // --rask-accent-soft custom properties are re-used across layout and page-level
    // scoped CSS, so changing them here cascades everywhere.
    private const string GlobalCss = """
                                     :root {
                                         --bs-primary: #0066B3;
                                         --bs-primary-rgb: 0, 102, 179;
                                         --bs-link-color: #0066B3;
                                         --bs-link-color-rgb: 0, 102, 179;
                                         --bs-link-hover-color: #00538F;
                                         --bs-link-hover-color-rgb: 0, 83, 143;
                                         --rask-accent: #0066B3;
                                         --rask-accent-strong: #00538F;
                                         --rask-accent-soft: #e3eff8;
                                     }
                                     .btn-primary {
                                         --bs-btn-bg: #0066B3;
                                         --bs-btn-border-color: #0066B3;
                                         --bs-btn-hover-bg: #00538F;
                                         --bs-btn-hover-border-color: #00538F;
                                         --bs-btn-active-bg: #004678;
                                         --bs-btn-active-border-color: #004678;
                                         --bs-btn-disabled-bg: #0066B3;
                                         --bs-btn-disabled-border-color: #0066B3;
                                     }
                                     .btn-outline-primary {
                                         --bs-btn-color: #0066B3;
                                         --bs-btn-border-color: #0066B3;
                                         --bs-btn-hover-bg: #0066B3;
                                         --bs-btn-hover-border-color: #0066B3;
                                         --bs-btn-active-bg: #0066B3;
                                         --bs-btn-active-border-color: #0066B3;
                                     }
                                     body { font-feature-settings: "ss01", "cv11"; }
                                     a { color: var(--rask-accent); }
                                     a:hover { color: var(--rask-accent-strong); }
                                     code, pre { font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
                                     :not(pre) > code {
                                         background: var(--rask-accent-soft);
                                         color: var(--rask-accent);
                                         padding: 0.08rem 0.32rem;
                                         border-radius: 4px;
                                         font-size: 0.86em;
                                     }
                                     pre { margin: 0; }
                                     .rask-badge {
                                         background: var(--rask-accent-soft);
                                         color: var(--rask-accent);
                                     }
                                     .text-accent { color: var(--rask-accent) !important; }
                                     """;

    protected override string? Css => GlobalCss;

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            Html("en")[
                Head()[
                    Meta("utf-8"),
                    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover"),
                    Title()["Rask — feature showcase"],
                    Link(Rel: "stylesheet",
                        Href: "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css",
                        CrossOrigin: "anonymous"),
                    Link(Rel: "stylesheet",
                        Href: "https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css",
                        CrossOrigin: "anonymous"),
                    Link(Rel: "stylesheet",
                        Href: "https://cdn.jsdelivr.net/gh/highlightjs/cdn-release@11.10.0/build/styles/atom-one-dark.min.css",
                        CrossOrigin: "anonymous"),
                    Script(Src: "https://cdn.jsdelivr.net/gh/highlightjs/cdn-release@11.10.0/build/highlight.min.js",
                        CrossOrigin: "anonymous"),
                    Script()[Raw("""
                        window.raskHighlight = function () {
                            if (!window.hljs) return;
                            document.querySelectorAll('pre code[class*="language-"]').forEach(function (el) {
                                delete el.dataset.highlighted;
                                hljs.highlightElement(el);
                            });
                        };
                        window.raskAfterMorph = window.raskHighlight;
                        if (document.readyState === "loading") {
                            document.addEventListener("DOMContentLoaded", window.raskHighlight);
                        } else {
                            window.raskHighlight();
                        }
                        """)],
                    RaskScopedStyles(),
                    Style()[Raw("""
                        html { -webkit-text-size-adjust: 100%; }
                        body {
                            -webkit-tap-highlight-color: transparent;
                            overscroll-behavior-y: none;
                            padding-left: env(safe-area-inset-left);
                            padding-right: env(safe-area-inset-right);
                        }
                        button, a, [role="button"] { touch-action: manipulation; }
                        """)]
                ],
                Body(Class: "bg-body-tertiary")[
                    Router(),
                    RaskRuntimeScript()
                ]
            ]
        ];
}
