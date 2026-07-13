using Microsoft.JSInterop;
using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Example.Site;

/// <summary>
/// Root of the Rask landing site — a WASM app that renders the whole marketing page in pure Rask
/// (RASK021 full shell). The interactive tiles (<see cref="LiveCounter"/>, <see cref="InstallTabs"/>)
/// are genuine stateful Rask components; the hero canvas packet-race, scroll reveals and the theme
/// toggle are driven by the sibling scoped module <c>App.js</c>, wired up in <see cref="OnRenderedAsync"/>.
/// Public + non-sealed to match the host's ActivatorUtilities + DAM contract.
/// </summary>
public class App : Component
{
    private readonly IJSRuntime _js;
    private readonly ElementRef _canvas = ElementRef.New();

    public App(IJSRuntime js) => _js = js;

    protected override Component? Head =>
    [
        Title()["Rask — web and native apps in pure C#"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1"),
        Meta(Name: "description", Content: "Rask is a C# component framework: one component model for the browser — server-rendered over WebSockets or client-side on WebAssembly — and native iOS/Android. No .razor, no XAML, no JSX, no JavaScript."),
        Meta(Name: "theme-color", Content: "#7c3aed"),
        Link(Rel: "icon", Type: "image/svg+xml", Href: LiveOptions.PathBase + "/icon.svg"),
        Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/global.css")
    ];

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        // Set up the hero canvas animation, IntersectionObserver reveals + bar growth, and the theme
        // toggle listener once the page is in the DOM. Everything document-level lives in App.js;
        // the counter and install tabs stay pure Rask state.
        if (firstRender)
            await _js.InvokeVoidAsync("Rask.App.init", _canvas);
    }

    private static Dictionary<string, string?> Attr(string key, string? value) => new() { [key] = value };

    protected override Component? Render() =>
    [
        Doctype(),
        Html("en")[
            Head(),
            Body()[
                TopBar(),
                Hero(),
                CounterSection(),
                BytesSection(),
                HostsSection(),
                FeaturesSection(),
                InstallSection(),
                FooterSection()
            ]
        ]
    ];

    // ---- top bar ----
    private Component TopBar() =>
        Div(Class: "topbar")[
            Div(Class: "wrap")[
                Span(Class: "brand")[Raw(BrandSvg), " Rask"],
                Nav()[
                    A(Class: "hide-sm", Href: "demo/", Target: "_blank", Rel: "noopener")["Demo"],
                    A(Class: "hide-sm", Href: "playground/", Target: "_blank", Rel: "noopener")["Playground"],
                    A(Class: "hide-sm", Href: "https://github.com/pal-tamas/rask/tree/main/docs", Target: "_blank", Rel: "noopener")["Docs"],
                    A(Href: "https://github.com/pal-tamas/rask", Target: "_blank", Rel: "noopener")["GitHub ↗"],
                    Button(Id: "themeToggle", Type: "button", Aria: Attr("label", "Toggle color theme"))["◐ theme"]
                ]
            ]
        ];

    // ---- hero ----
    private Component Hero() =>
        Section(Class: "hero")[
            Div(Class: "wrap")[
                Div(Class: "hero-grid")[
                    Div()[
                        P(Class: "eyebrow")["A C# component framework"],
                        H1()["Web and native apps,", Br(), "in ", Span(Class: "lit")["pure C#"], "."],
                        P(Class: "lede")["One component model for the browser and the phone — no ", Code()[".razor"], ", no XAML, no JSX, no JavaScript, no Swift or Kotlin."],
                        P(Class: "sub")["The same component runs server-rendered over a WebSocket, fully client-side on WebAssembly, or as a native iOS/Android app. One codebase; pick the host per project."],
                        Div(Class: "cta-row")[
                            A(Class: "btn btn-primary", Href: "demo/", Target: "_blank", Rel: "noopener")["▶ Try the live demo"],
                            A(Class: "btn btn-ghost", Href: "playground/", Target: "_blank", Rel: "noopener")["🛝 Playground"]
                        ],
                        Div(Class: "badges")[
                            Span(Class: "badge")[B()[".NET 10"]],
                            Span(Class: "badge")["MIT"],
                            Span(Class: "badge")[B()["Server"], " · WASM · Native"],
                            Span(Class: "badge")["Leads on ", B()["every measured axis"]]
                        ]
                    ],
                    Div(Class: "wire")[
                        Div(Class: "wire-head")[
                            Span(Class: "lab")["wire · one state change"],
                            Span(Class: "lab")["live diff"]
                        ],
                        Canvas(Width: 820, Height: 300, Class: "wire-cvs", Ref: _canvas,
                            Aria: Attr("label", "A full 24 KB page versus Rask's tiny 41-byte diff traveling the wire")),
                        Div(Class: "wire-legend")[
                            Span()[Span(Class: "dot", Style: "background:var(--blazor)"), "full page ", B()["24 KB"]],
                            Span()[Span(Class: "dot", Style: "background:var(--accent)"), "Rask diff ", B()["~41 B"]]
                        ],
                        Div(Class: "wire-tape")[
                            Span(Class: "k")["counter tick on a 24 KB page"], Span(Class: "v")["24,114 B → ", B(Class: "mono")["41 B"]],
                            Span(Class: "k")["smaller than re-sending the page"], Span(Class: "v win")["588×"],
                            Span(Class: "k")["allocated / update"], Span(Class: "v win")["~40× less"],
                            Span(Class: "k")["bytes that ever leave the server"], Span(Class: "v win")["just the diff"]
                        ]
                    ]
                ]
            ]
        ];

    // ---- "one C# class" demo ----
    private Component CounterSection() =>
        Section()[
            Div(Class: "wrap")[
                Div(Class: "sec-head reveal")[
                    P(Class: "eyebrow")["A component is a class that returns a tree"],
                    H2()["Routing, state, and events — one C# class."],
                    P()["No template dialect, no code-behind, no build step for markup. ", Code()["Div(...)[Span(...), \"hi\"]"], " is plain, refactor-safe, IDE-native C#. Here's a complete, routable, interactive component:"]
                ],
                Div(Class: "demo-grid reveal")[
                    Div(Class: "card")[
                        Div(Class: "card-bar")[
                            Span(Class: "traf", Style: "background:#ff5f57"),
                            Span(Class: "traf", Style: "background:#febc2e"),
                            Span(Class: "traf", Style: "background:#28c840"),
                            Span(Class: "fn")["Counter.cs"]
                        ],
                        Pre()[Code()[Raw(CounterCodeHtml)]]
                    ],
                    // The live tile is a real stateful Rask component — the page proving its own thesis.
                    LiveCounter()
                ]
            ]
        ];

    // ---- bytes / benchmarks ----
    private Component Bar(string kind, int h, string cap) =>
        Div(Class: "bar " + kind, Data: Attr("h", h.ToString()))[Span(Class: "cap")[cap]];

    private Component BarCol(string blazorCap, int blazorH, string raskCap, int raskH, string mult, string label) =>
        Div(Class: "bar-col")[
            Div(Class: "bar-stack")[Bar("blazor", blazorH, blazorCap), Bar("rask", raskH, raskCap)],
            Span(Class: "x")[B()[mult], label]
        ];

    private static Component Stat(string kpi, string lab, string sub) =>
        Div(Class: "stat")[Div(Class: "kpi")[kpi], Div(Class: "lab")[lab], Div(Class: "sub")[sub]];

    private Component BytesSection() =>
        Section()[
            Div(Class: "wrap")[
                Div(Class: "sec-head reveal")[
                    P(Class: "eyebrow")["Rask vs Blazor · CI-enforced baselines"],
                    H2()["Fewer bytes than Blazor — on every scenario."],
                    P()["Rask treats the network as the real bottleneck: after first paint, a state change ships a minimal diff. Each pair is the ", B()["same"], " state change — Blazor's payload beside Rask's. The number is how many ", B()["× fewer bytes"], " Rask puts on the wire."]
                ],
                Div(Class: "bars-panel reveal")[
                    Div(Id: "bars", Class: "bars")[
                        BarCol("186 B", 240, "41 B", 53, "4.5×", "Counter / 24 KB page"),
                        BarCol("1,722 B", 240, "137 B", 19, "12.6×", "Deep-tree tick"),
                        BarCol("6,522 B", 240, "441 B", 16, "14.8×", "Deep mutation ×200"),
                        BarCol("2,080 B", 240, "37 B", 5, "56×", "Remove 100 rows")
                    ],
                    Div(Class: "bars-legend")[
                        Span()[Span(Class: "dot", Style: "background:var(--blazor)"), "Blazor — full payload"],
                        Span()[Span(Class: "dot", Style: "background:var(--accent)"), "Rask — the diff"]
                    ]
                ],
                Div(Class: "stat-row reveal")[
                    Stat("~41 B", "Bytes on the wire", "counter on a 24 KB page · vs 186 B"),
                    Stat("~40×", "Less allocated / update", "1,072 B · vs Blazor 42,972 B"),
                    Stat("~30%", "Leaner retained heap", "158 KB · vs 224 KB (200 rows)"),
                    Stat("1.76×", "Faster render hot path", "598 ns · vs 1,052 ns")
                ],
                P(Class: "honest reveal")["Retained heap used to be Blazor's one win — a pure-element page now keeps a compact frame snapshot instead of an object-per-element graph, so ", B()["Rask leads on every measured axis."], " Numbers from the CI-enforced ", A(Href: "https://github.com/pal-tamas/rask/blob/main/benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md", Target: "_blank", Rel: "noopener")["vs-blazor baselines"], " (Apple M4, .NET 10)."]
            ]
        ];

    // ---- hosts ----
    private static Component Host(string tag, string title, string prev, params Component?[] body) =>
        Div(Class: "host")[
            Span(Class: "tag")[tag],
            H3()[title],
            P()[body],
            Span(Class: "prev")[prev]
        ];

    private Component HostsSection() =>
        Section()[
            Div(Class: "wrap")[
                Div(Class: "sec-head reveal")[
                    P(Class: "eyebrow")["One component model · three hosts"],
                    H2()["Write it once. Ship it where you need it."],
                    P()["The identical C# component runs unchanged across every host — you choose the runtime per project, not per component."]
                ],
                Div(Class: "hosts reveal")[
                    Host("Rask.Server", "Server", "AddRask() · UseRask<TApp>()",
                        "ASP.NET host. State lives on the server; a live diff streams to the browser over a WebSocket. Nothing to compile client-side."),
                    Host("Rask.Wasm", "WebAssembly", "WasmHostBuilder.CreateDefault()",
                        "The same component runs fully client-side on the browser's Mono/WASM runtime via JSImport/JSExport. Ships as an installable, offline PWA."),
                    Host("Rask.Native · preview", "Native iOS / Android", "NativeAppHost.CreateDefault()",
                        "A WebView-hybrid app head for App Store / Play Store — your C# runs natively on the device. Scaffold with ", Code()["dotnet new rask-native"], ".")
                ]
            ]
        ];

    // ---- features ----
    private static Component Feature(string glyph, string title, params Component?[] desc) =>
        Div(Class: "f")[
            Div(Class: "fh")[Span(Class: "b")[glyph], " ", title],
            P()[desc]
        ];

    private Component FeaturesSection() =>
        Section()[
            Div(Class: "wrap")[
                Div(Class: "sec-head reveal")[
                    P(Class: "eyebrow")["Batteries included · all type-safe"],
                    H2()["A full framework, generated at compile time."],
                    P()["Roslyn source generators build per-component factories and typed route URLs — trim-safe, reflection-free, and checked by 30+ compile-time diagnostics."]
                ],
                Div(Class: "feat reveal")[
                    Feature("⌁", "Source generators", "Per-component ", Code()["{Type}(...)"], " factories and type-safe ", Code()["Routes.*"], " URL builders. Rename a route, break the build — never a dead link."),
                    Feature("◑", "Scoped CSS & JS", "Drop a sibling ", Code()["{Component}.css"], "/", Code()[".js"], ". Auto-scoped, no leaks, no class-name discipline — a mismatch is a build error."),
                    Feature("▤", "Forms & validation", Code()["Form<T>"], " with two-way binding, plus inline, DataAnnotations, FluentValidation, and async validators."),
                    Feature("⚿", "Auth, four ways", "Cookie & JWT on both Server and WASM, route guards, and an ", Code()["--auth"], " template switch. Identity, Keycloak, Auth0, OIDC."),
                    Feature("⇄", "CQRS", "Source-generated, trim-safe queries, commands, notifications and pipeline behaviors via ", Code()["AddRaskCqrs()"], " — standalone, zero reflection."),
                    Feature("▚", "PWA & Web Push", "Typed manifest, a default service worker, and VAPID/RFC-8291 Web Push with zero external deps. ", Code()["--pwa"], " and you're installable."),
                    Feature("◈", "43 typed browser APIs", "Storage, clipboard, geolocation, passkeys, share, sensors, observers, serial/USB/HID/Bluetooth — one awaitable C# layer, identical on Server & WASM."),
                    Feature("⌂", "Secure by default", "Strings are HTML-encoded, URL attributes are scheme-sanitized (", Code()["javascript:"], " → ", Code()["about:blank"], "). Safe output is the default, not a flag."),
                    Feature("↻", "C# Hot Reload", "Edit ", Code()["Render()"], " or scoped css/js under ", Code()["dotnet watch"], " and it re-renders live — the closest a compiled framework gets to a no-build loop.")
                ]
            ]
        ];

    // ---- install ----
    private Component InstallSection() =>
        Section()[
            Div(Class: "wrap")[
                Div(Class: "sec-head reveal", Style: "text-align:center; margin-left:auto; margin-right:auto;")[
                    P(Class: "eyebrow", Style: "justify-content:center;")["Prerequisite · .NET 10 SDK"],
                    H2()["Up and running in one command."]
                ],
                Div(Class: "reveal")[InstallTabs()]
            ]
        ];

    // ---- footer ----
    private Component FooterSection() =>
        Footer()[
            Div(Class: "wrap")[
                Div(Class: "foot-cta reveal")[
                    H2()["The docs and the live demo are the real tour."],
                    P()["This is just the front door. Click through a full multi-page Rask app in the browser, or write a component live in the playground."],
                    Div(Class: "cta-row", Style: "justify-content:center;")[
                        A(Class: "btn btn-primary", Href: "demo/", Target: "_blank", Rel: "noopener")["▶ Open the live demo"],
                        A(Class: "btn btn-ghost", Href: "https://github.com/pal-tamas/rask", Target: "_blank", Rel: "noopener")["★ Star on GitHub"]
                    ],
                    Div(Class: "foot-links")[
                        A(Href: "demo/", Target: "_blank", Rel: "noopener")["Live demo"],
                        A(Href: "playground/", Target: "_blank", Rel: "noopener")["Playground"],
                        A(Href: "https://github.com/pal-tamas/rask/tree/main/docs", Target: "_blank", Rel: "noopener")["Docs"],
                        A(Href: "https://www.nuget.org/packages/Rask.Server", Target: "_blank", Rel: "noopener")["NuGet"],
                        A(Href: "https://github.com/pal-tamas/rask", Target: "_blank", Rel: "noopener")["GitHub"]
                    ],
                    P(Class: "foot-meta")[Span(Class: "bolt")["⚡"], " Rask — Norwegian / Danish / Swedish for ", B()["fast"], ". Built with .NET 10 · MIT."]
                ]
            ]
        ];

    private const string BrandSvg =
        "<svg viewBox=\"0 0 128 128\" aria-hidden=\"true\"><defs><linearGradient id=\"tb\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#8b5cf6\"/><stop offset=\"1\" stop-color=\"#7c3aed\"/></linearGradient></defs><rect width=\"128\" height=\"128\" rx=\"28\" fill=\"url(#tb)\"/><path d=\"M74 24 L38 66 L58 66 L53 104 L92 58 L70 58 Z\" fill=\"#fff\"/></svg>";

    // Static, trusted syntax-highlighted markup for the Counter.cs sample (global .t-* classes).
    private const string CounterCodeHtml =
        """
        <span class="t-attr">[Route(<span class="t-str">"/counter"</span>)]</span>
        <span class="t-key">public sealed class</span> <span class="t-type">Counter</span> : <span class="t-type">Component</span>
        {
            <span class="t-key">private int</span> _count;

            <span class="t-key">protected override</span> <span class="t-type">Component</span>? <span class="t-fn">Render</span>() =&gt;
            [
                <span class="t-type">H1</span>()[<span class="t-str">"Counter"</span>],
                <span class="t-type">P</span>()[<span class="t-str">$"Current count: {_count}"</span>],
                <span class="t-type">Button</span>(<span class="t-attr">OnClick:</span> () =&gt; _count++)[<span class="t-str">"Click me"</span>]
            ];
        }
        """;
}
