using Microsoft.JSInterop;
using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Example.Site;

/// <summary>
/// Root of the Rask landing site — a WASM app that renders the whole marketing page in pure Rask,
/// straight into the framework's &lt;body&gt;. The interactive tiles (<see cref="LiveCounter"/>, <see cref="InstallTabs"/>)
/// are genuine stateful Rask components; the hero canvas packet-race, scroll reveals and the theme
/// toggle are driven by the sibling scoped module <c>App.js</c>, wired up in <see cref="OnRenderedAsync"/>.
/// Public + non-sealed to match the host's ActivatorUtilities + DAM contract.
/// </summary>
public partial class App : Component
{
    private readonly IJSRuntime _js;
    private readonly ElementRef _canvas = ElementRef.New();

    public App(IJSRuntime js) => _js = js;

    protected override Component? HeadAssets =>
    [
        Title["Rask — the .NET One Person Framework"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Meta
            .Name("description")
            .Content("Rask is the .NET One Person Framework: one developer builds, runs, and ships a whole product — UI, data, auth, background work, and deploy — from one C# codebase on one SQLite-backed server. The same components run on Server, WebAssembly, and native iOS/Android."),
        Meta.Name("theme-color").Content("#7c3aed"),
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/global.css")
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
        TopBar(),
        Hero(),
        CounterSection(),
        BytesSection(),
        HostsSection(),
        FeaturesSection(),
        WholeBackEndSection(),
        InstallSection(),
        FooterSection()
    ];

    // ---- top bar ----
    private Component TopBar() =>
        Div.Class("topbar")[
            Div.Class("wrap")[
                Span.Class("brand")[Raw.Value(BrandSvg), " Rask"],
                Nav[
                    A.Class("hide-sm").Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                    A.Class("hide-sm").Href("playground/").Target("_blank").Rel("noopener")["Playground"],
                    A.Href("https://github.com/pal-tamas/rask").Target("_blank").Rel("noopener")["GitHub ↗"],
                    Button.Id("themeToggle").Type("button").Aria(Attr("label", "Toggle color theme"))["◐ theme"]
                ]
            ]
        ];

    // ---- hero ----
    private Component Hero() =>
        Section.Class("hero")[
            Div.Class("wrap")[
                Div.Class("hero-grid")[
                    Div[
                        P.Class("eyebrow")["The .NET One Person Framework"],
                        H1["Ship a whole product.", Br, "Just you, and ", Span.Class("lit")["C#"], "."],
                        P.Class("lede")["Build, run, and ship a complete product — the UI, the data, the auth, the background work, and the deploy — from one C# codebase on one server."],
                        P.Class("sub")["The same components run server-rendered over a WebSocket, fully client-side on WebAssembly, or as a native iOS/Android app — no ", Code[".razor"], ", no JavaScript, no second language. SQLite is the production database; one box runs the whole thing."],
                        Div.Class("cta-row")[
                            A.Class("btn btn-primary").Href("docs/").Target("_blank").Rel("noopener")["▶ Try the live demo"],
                            A.Class("btn btn-ghost").Href("playground/").Target("_blank").Rel("noopener")["🛝 Playground"]
                        ],
                        Div.Class("badges")[
                            Span.Class("badge")[B[".NET 10"]],
                            Span.Class("badge")["MIT"],
                            Span.Class("badge")[B["Server"], " · WASM · Native"],
                            Span.Class("badge")[B["SQLite"], " · production DB"]
                        ]
                    ],
                    Div.Class("wire")[
                        Div.Class("wire-head")[
                            Span.Class("lab")["wire · one state change"],
                            Span.Class("lab")["live diff"]
                        ],
                        Canvas
                            .Width(820)
                            .Height(300)
                            .Class("wire-cvs")
                            .Ref(_canvas)
                            .Aria(Attr("label", "A full 24 KB page versus Rask's tiny 41-byte diff traveling the wire")),
                        Div.Class("wire-legend")[
                            Span[Span.Class("dot").Style("background:var(--blazor)"), "full page ", B["24 KB"]],
                            Span[Span.Class("dot").Style("background:var(--accent)"), "Rask diff ", B["~41 B"]]
                        ],
                        Div.Class("wire-tape")[
                            Span.Class("k")["counter tick on a 24 KB page"], Span.Class("v")["24,114 B → ", B.Class("mono")["41 B"]],
                            Span.Class("k")["smaller than re-sending the page"], Span.Class("v win")["588×"],
                            Span.Class("k")["allocated / update"], Span.Class("v win")["~40× less"],
                            Span.Class("k")["bytes that ever leave the server"], Span.Class("v win")["just the diff"]
                        ]
                    ]
                ]
            ]
        ];

    // ---- "one C# class" demo ----
    private Component CounterSection() =>
        Section[
            Div.Class("wrap")[
                Div.Class("sec-head reveal")[
                    P.Class("eyebrow")["A component is a class that returns a tree"],
                    H2["Routing, state, and events — one C# class."],
                    P["No template dialect, no code-behind, no build step for markup. Markup is a chain — name a component and dot onto it, ", Code["Div.Class(\"card\")[Span[\"hi\"]]"], " — so the IDE lists every step as you type it. Plain, refactor-safe, IDE-native C#. Here's a complete, routable, interactive component:"]
                ],
                Div.Class("demo-grid reveal")[
                    Div.Class("card")[
                        Div.Class("card-bar")[
                            Span.Class("traf").Style("background:#ff5f57"),
                            Span.Class("traf").Style("background:#febc2e"),
                            Span.Class("traf").Style("background:#28c840"),
                            Span.Class("fn")["Counter.cs"]
                        ],
                        Pre[Code[Raw.Value(CounterCodeHtml)]]
                    ],
                    // The live tile is a real stateful Rask component — the page proving its own thesis.
                    LiveCounter
                ]
            ]
        ];

    // ---- bytes / benchmarks ----
    private Component Bar(string kind, int h, string cap) =>
        Div.Class("bar " + kind).Data(Attr("h", h.ToString()))[Span.Class("cap")[cap]];

    private Component BarCol(string blazorCap, int blazorH, string raskCap, int raskH, string mult, string label) =>
        Div.Class("bar-col")[
            Div.Class("bar-stack")[Bar("blazor", blazorH, blazorCap), Bar("rask", raskH, raskCap)],
            Span.Class("x")[B[mult], label]
        ];

    private static Component Stat(string kpi, string lab, string sub) =>
        Div.Class("stat")[Div.Class("kpi")[kpi], Div.Class("lab")[lab], Div.Class("sub")[sub]];

    private Component BytesSection() =>
        Section[
            Div.Class("wrap")[
                Div.Class("sec-head reveal")[
                    P.Class("eyebrow")["Rask vs Blazor · CI-enforced baselines"],
                    H2["Fewer bytes than Blazor — on every scenario."],
                    P["Rask treats the network as the real bottleneck: after first paint, a state change ships a minimal diff. Each pair is the ", B["same"], " state change — Blazor's payload beside Rask's. The number is how many ", B["× fewer bytes"], " Rask puts on the wire."]
                ],
                Div.Class("bars-panel reveal")[
                    Div.Id("bars").Class("bars")[
                        BarCol("186 B", 240, "41 B", 53, "4.5×", "Counter / 24 KB page"),
                        BarCol("1,722 B", 240, "137 B", 19, "12.6×", "Deep-tree tick"),
                        BarCol("6,522 B", 240, "441 B", 16, "14.8×", "Deep mutation ×200"),
                        BarCol("2,080 B", 240, "37 B", 5, "56×", "Remove 100 rows")
                    ],
                    Div.Class("bars-legend")[
                        Span[Span.Class("dot").Style("background:var(--blazor)"), "Blazor — full payload"],
                        Span[Span.Class("dot").Style("background:var(--accent)"), "Rask — the diff"]
                    ]
                ],
                Div.Class("stat-row reveal")[
                    Stat("~41 B", "Bytes on the wire", "counter on a 24 KB page · vs 186 B"),
                    Stat("~40×", "Less allocated / update", "1,072 B · vs Blazor 42,972 B"),
                    Stat("~30%", "Leaner retained heap", "158 KB · vs 224 KB (200 rows)"),
                    Stat("1.76×", "Faster render hot path", "598 ns · vs 1,052 ns")
                ],
                P.Class("honest reveal")["Retained heap used to be Blazor's one win — a pure-element page now keeps a compact frame snapshot instead of an object-per-element graph, so ", B["Rask leads on every measured axis."], " Numbers from the CI-enforced ", A
                    .Href("https://github.com/pal-tamas/rask/blob/main/benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md")
                    .Target("_blank")
                    .Rel("noopener")["vs-blazor baselines"], " (Apple M4, .NET 10)."]
            ]
        ];

    // ---- hosts ----
    private static Component Host(string tag, string title, string prev, params Component?[] body) =>
        Div.Class("host")[
            Span.Class("tag")[tag],
            H3[title],
            P[body],
            Span.Class("prev")[prev]
        ];

    private Component HostsSection() =>
        Section[
            Div.Class("wrap")[
                Div.Class("sec-head reveal")[
                    P.Class("eyebrow")["One component model · three hosts"],
                    H2["Write it once. Ship it where you need it."],
                    P["The identical C# component runs unchanged across every host — you choose the runtime per project, not per component."]
                ],
                Div.Class("hosts reveal")[
                    Host("Rask.Server", "Server", "AddRask() · UseRask<TApp>()",
                        "ASP.NET host. State lives on the server; a live diff streams to the browser over a WebSocket. Nothing to compile client-side."),
                    Host("Rask.Wasm", "WebAssembly", "WasmHostBuilder.CreateDefault()",
                        "The same component runs fully client-side on the browser's Mono/WASM runtime via JSImport/JSExport. Ships as an installable, offline PWA."),
                    Host("Rask.Native · preview", "Native iOS / Android", "NativeAppHost.CreateDefault()",
                        "A WebView-hybrid app head for App Store / Play Store — your C# runs natively on the device. Scaffold with ", Code["rask new MyApp --template native"], ".")
                ]
            ]
        ];

    // ---- features ----
    private static Component Feature(string glyph, string title, params Component?[] desc) =>
        Div.Class("f")[
            Div.Class("fh")[Span.Class("b")[glyph], " ", title],
            P[desc]
        ];

    private Component FeaturesSection() =>
        Section[
            Div.Class("wrap")[
                Div.Class("sec-head reveal")[
                    P.Class("eyebrow")["Batteries included · all type-safe"],
                    H2["A full framework, generated at compile time."],
                    P["Roslyn source generators build each component's chain surface and typed route URLs — trim-safe, reflection-free, and checked by 30+ compile-time diagnostics."]
                ],
                Div.Class("feat reveal")[
                    Feature("⌁", "Source generators", "A chain surface per component — ", Code["BsCard.Title(…)"], " — that demands what the component can't do without, plus type-safe ", Code["Routes.*"], " URL builders. Rename a route, break the build — never a dead link."),
                    Feature("◑", "Scoped CSS & JS", "Drop a sibling ", Code["{Component}.css"], "/", Code[".js"], ". Auto-scoped, no leaks, no class-name discipline — a mismatch is a build error."),
                    Feature("▤", "Forms & validation", Code["Form<T>"], " with two-way binding, plus inline, DataAnnotations, FluentValidation, and async validators."),
                    Feature("⚿", "Auth, four ways", "Cookie & JWT on both Server and WASM, route guards, and an ", Code["--auth"], " template switch. Identity, Keycloak, Auth0, OIDC."),
                    Feature("⇄", "CQRS", "Source-generated, trim-safe queries, commands, notifications and pipeline behaviors via ", Code["AddRaskCqrs()"], " — standalone, zero reflection."),
                    Feature("▚", "PWA & Web Push", "Typed manifest, a default service worker, and VAPID/RFC-8291 Web Push with zero external deps. ", Code["--pwa"], " and you're installable."),
                    Feature("◈", "50 typed browser APIs", "Storage, clipboard, geolocation, passkeys, share, sensors, observers, serial/USB/HID/Bluetooth — one awaitable C# layer, identical on Server & WASM."),
                    Feature("⌂", "Secure by default", "Strings are HTML-encoded, URL attributes are scheme-sanitized (", Code["javascript:"], " → ", Code["about:blank"], "). Safe output is the default, not a flag."),
                    Feature("↻", "C# Hot Reload", "Edit ", Code["Render()"], " or scoped css/js under ", Code["dotnet watch"], " and it re-renders live — the closest a compiled framework gets to a no-build loop.")
                ]
            ]
        ];

    // ---- one person's whole back end ----
    private Component WholeBackEndSection() =>
        Section[
            Div.Class("wrap")[
                Div.Class("sec-head reveal")[
                    P.Class("eyebrow")["DB-backed by default · no external services"],
                    H2["One person's whole back end."],
                    P["Behind the same C# UI, every stateful pillar rides the app's own SQLite database — no broker, no Redis, no second service to run. Adding one is a package reference, not a new box to operate."]
                ],
                Div.Class("feat reveal")[
                    Feature("⊞", "A feature slice", "A CQRS + EF Core CRUD slice — entity, validation, list/create/edit pages, and tests — written once in the tutorial and repeated per feature. Small enough to type, so nothing is generated you can't read."),
                    Feature("◷", "Background jobs", "Durable enqueued, delayed, and recurring work on your database, run by a hosted worker — at-least-once, with exponential backoff."),
                    Feature("✉", "Transactional email", "Email queued on the same database and delivered over SMTP off the request thread; bodies are Rask components."),
                    Feature("⤴", "Transactional outbox", "Domain events captured in the same transaction as your data and relayed at-least-once — crash-safe, no message broker."),
                    Feature("⚡", "Cache", "A database-backed ", Code["IDistributedCache"], " plus a typed ", Code["ICache"], " with ", Code["GetOrCreateAsync"], " and sliding/absolute expiry."),
                    Feature("⬢", "Production SQLite", "SQLite as the production database — WAL + busy-timeout pragmas (~99k ops/s on a laptop), continuous Litestream backup, scheduled snapshots."),
                    Feature("⬈", "One-command deploy", Code["rask deploy"], " takes a bare VPS to a live HTTPS site — Docker, a non-root deploy user, firewall + SSH hardening, and zero-downtime swaps."),
                    Feature("◎", "Web Push", "Send Web Push from your backend on your own VAPID keys (RFC 8292/8291) — zero external dependencies.")
                ]
            ]
        ];

    // ---- install ----
    private Component InstallSection() =>
        Section[
            Div.Class("wrap")[
                Div.Class("sec-head reveal").Style("text-align:center; margin-left:auto; margin-right:auto;")[
                    P.Class("eyebrow").Style("justify-content:center;")["Prerequisite · .NET 10 SDK"],
                    H2["Up and running in one command."]
                ],
                Div.Class("reveal")[InstallTabs]
            ]
        ];

    // ---- footer ----
    private Component FooterSection() =>
        Footer[
            Div.Class("wrap")[
                Div.Class("foot-cta reveal")[
                    H2["The live docs and the playground are the real tour."],
                    P["This is just the front door. Click through a full multi-page Rask app in the browser, or write a component live in the playground."],
                    Div.Class("cta-row").Style("justify-content:center;")[
                        A.Class("btn btn-primary").Href("docs/").Target("_blank").Rel("noopener")["▶ Open the live demo"],
                        A
                            .Class("btn btn-ghost")
                            .Href("https://github.com/pal-tamas/rask")
                            .Target("_blank")
                            .Rel("noopener")["★ Star on GitHub"]
                    ],
                    Div.Class("foot-links")[
                        A.Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                        A.Href("playground/").Target("_blank").Rel("noopener")["Playground"],
                        A.Href("https://www.nuget.org/packages/Rask.Server").Target("_blank").Rel("noopener")["NuGet"],
                        A.Href("https://github.com/pal-tamas/rask").Target("_blank").Rel("noopener")["GitHub"]
                    ],
                    P.Class("foot-meta")[Span.Class("bolt")["⚡"], " Rask — Norwegian / Danish / Swedish for ", B["fast"], ". Built with .NET 10 · MIT."]
                ]
            ]
        ];

    private const string BrandSvg =
        "<svg viewBox=\"0 0 128 128\" aria-hidden=\"true\"><defs><linearGradient id=\"tb\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#8b5cf6\"/><stop offset=\"1\" stop-color=\"#7c3aed\"/></linearGradient></defs><rect width=\"128\" height=\"128\" rx=\"28\" fill=\"url(#tb)\"/><path d=\"M74 24 L38 66 L58 66 L53 104 L92 58 L70 58 Z\" fill=\"#fff\"/></svg>";

    // Static, trusted syntax-highlighted markup for the Counter.cs sample (global .t-* classes).
    private const string CounterCodeHtml =
        """
        <span class="t-key">public sealed partial class</span> <span class="t-type">Counter</span> : <span class="t-type">Page</span>
        {
            <span class="t-key">protected override string</span> Route =&gt; <span class="t-str">"/counter"</span>;

            <span class="t-key">private int</span> _count;

            <span class="t-key">protected override</span> <span class="t-type">Component</span>? <span class="t-fn">Render</span>() =&gt;
            [
                <span class="t-type">H1</span>[<span class="t-str">"Counter"</span>],
                <span class="t-type">P</span>[<span class="t-str">$"Current count: {_count}"</span>],
                <span class="t-type">Button</span>.<span class="t-fn">OnClick</span>(() =&gt; _count++)[<span class="t-str">"Click me"</span>]
            ];
        }
        """;
}
