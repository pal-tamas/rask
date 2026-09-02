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

    // Which journey the architecture map is holding. State on the page, not in the SVG: switching it
    // re-renders and ships one changed class attribute.
    private FlowScene _scene = FlowScene.ServerRoundTrip;

    public App(IJSRuntime js) => _js = js;

    protected override Component? HeadAssets =>
    [
        Title["Rask — the .NET One Person Framework"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Meta
            .Name("description")
            .Content("Rask is the .NET One Person Framework: one developer builds, runs, and ships a whole product — UI, data, auth, background work, and deploy — from one C# codebase on one SQLite-backed server. The same components run on Server and WebAssembly."),
        Meta.Name("theme-color").Content("#7c3aed"),
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
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

    // The page's vocabulary. Constants rather than @apply: @apply moves the decision into a stylesheet
    // Tailwind then has to be told about, which is the coupling this rewrite removed. A constant is read
    // by the compiler, renamed by the IDE, and found by Tailwind's scanner like any other literal.
    private const string Wrap = "mx-auto w-full max-w-[1080px] px-6";

    private const string Eyebrow =
        "mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-accent-ink";

    private const string Lede = "mt-5 text-lg leading-relaxed text-ink-soft";

    private const string Sub = "mt-4 text-sm leading-relaxed text-muted";

    private const string Card = "rounded-2xl border border-line bg-panel";

    private const string Badge =
        "rounded-full border border-line bg-panel px-3 py-1 text-xs text-ink-soft";

    // Btn, not Button: a constant named Button would shadow the Button chain entry inside this markup
    // host, and every <button> on the page would then need qualifying.
    private const string Btn =
        "inline-flex items-center gap-2 rounded-xl px-5 py-3 text-sm font-semibold no-underline transition";

    private const string BtnPrimary = Btn + " bg-accent text-white hover:bg-accent-2";

    private const string BtnGhost =
        Btn + " border border-line text-ink hover:border-accent hover:text-accent-ink";

    private const string SectionPad = "py-20 sm:py-24";

    private const string H2Class = "mt-2 text-3xl font-semibold tracking-tight text-ink sm:text-4xl";

    protected override Component? Render() =>
    [
        TopBar(),
        Hero(),
        CounterSection(),
        BytesSection(),
        HostsSection(),
        HowItFitsSection(),
        FeaturesSection(),
        WholeBackEndSection(),
        InstallSection(),
        FooterSection()
    ];

    // ---- top bar ----
    private Component TopBar() =>
        Div.Class("sticky top-0 z-50 border-b border-line bg-ground/80 backdrop-blur")[
            Div.Class($"{Wrap} flex h-16 items-center justify-between")[
                Span.Class("flex items-center gap-2 text-lg font-semibold tracking-tight text-ink [&>svg]:size-7 [&>svg]:rounded-lg")[
                    Raw.Value(BrandSvg), " Rask"
                ],
                Nav.Class("flex items-center gap-5 text-sm text-slate-500 dark:text-slate-400")[
                    // Hidden on a narrow viewport rather than wrapped: the bar is chrome, and three links
                    // stacking over two lines pushes the hero below the fold on a phone.
                    A.Class("hidden no-underline hover:text-ink sm:inline").Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                    A.Class("hidden no-underline hover:text-ink sm:inline").Href("playground/").Target("_blank").Rel("noopener")["Playground"],
                    A.Class("no-underline hover:text-ink").Href("https://github.com/pal-tamas/rask").Target("_blank").Rel("noopener")["GitHub ↗"],
                    Button
                        .Id("themeToggle")
                        .Type("button")
                        .Class("rounded-lg border border-line px-2.5 py-1.5 text-xs text-slate-500 dark:text-slate-400 hover:text-ink")
                        .Aria(Attr("label", "Toggle color theme"))["◐ theme"]
                ]
            ]
        ];

    // ---- hero ----
    private Component Hero() =>
        Section.Class("pt-16 pb-20 sm:pt-24 sm:pb-28")[
            Div.Class(Wrap)[
                // The page opens on the whole product, before it says a word about the framework — the
                // same animation the README leads with, rendered INLINE (not through <img>) so it
                // inherits the token palette below and follows the theme toggle. See FlowAnimation for
                // why that works. Unpinned here: the hero cycles every journey by itself, because a hero
                // that needs to be operated is a hero nobody operates. The picker lives further down, in
                // HowItFits, for a reader who has decided which journey they care about.
                // hero-anim/hero-grid are hooks, not styling: SiteExampleTests asserts the adjacency
                // ".hero-anim + .hero-grid h1", and a Playwright locator that resolves to nothing fails by
                // timing out rather than by naming what went missing.
                Div.Class("hero-anim mb-12 flex justify-center")[FlowAnimation],
                Div.Class("hero-grid grid items-center gap-12 lg:grid-cols-2")[
                    Div[
                        P.Class(Eyebrow)["The .NET One Person Framework"],
                        H1.Class("text-4xl font-semibold leading-[1.1] tracking-tight text-ink sm:text-5xl")[
                            "Ship a whole product.", Br, "Just you, and ",
                            Span.Class("text-accent-ink")["C#"], "."
                        ],
                        P.Class(Lede)["Build, run, and ship a complete product — the UI, the data, the auth, the background work, and the deploy — from one C# codebase on one server."],
                        P.Class(Sub)["The same components run server-rendered over a WebSocket or fully client-side on WebAssembly — no ", Code[".razor"], ", no JavaScript, no second language. SQLite is the production database; one box runs the whole thing."],
                        Div.Class("mt-8 flex flex-wrap gap-3")[
                            // Named for what they are. The Pages site is three apps — this landing page at
                            // the root, the live showcase at docs/, the playground at playground/ — and
                            // calling docs/ "the live demo" left the docs themselves with no name at all.
                            A.Id("cta-docs").Class(BtnPrimary).Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                            A.Class(BtnGhost).Href("playground/").Target("_blank").Rel("noopener")["Playground"]
                        ],
                        Div.Class("mt-8 flex flex-wrap gap-2")[
                            Span.Class(Badge)[B[".NET 10"]],
                            Span.Class(Badge)["MIT"],
                            Span.Class(Badge)[B["Server"], " · WASM"],
                            Span.Class(Badge)[B["SQLite"], " · production DB"]
                        ]
                    ],
                    Div.Class($"{Card} overflow-hidden p-4")[
                        Div.Class("mb-3 flex items-center justify-between text-[0.7rem] uppercase tracking-widest text-slate-500 dark:text-slate-400")[
                            Span["wire · one state change"],
                            Span["live diff"]
                        ],
                        Canvas
                            .Width(820)
                            .Height(300)
                            .Class("h-auto w-full rounded-xl bg-panel-2")
                            .Ref(_canvas)
                            .Aria(Attr("label", "A full 24 KB page versus Rask's tiny 41-byte diff traveling the wire")),
                        Div.Class("mt-3 flex flex-wrap gap-4 text-xs text-slate-500 dark:text-slate-400")[
                            Span.Class("flex items-center gap-2")[Dot("var(--color-blazor)"), "full page ", B["24 KB"]],
                            Span.Class("flex items-center gap-2")[Dot("var(--color-accent)"), "Rask diff ", B["~41 B"]]
                        ],
                        Dl.Class("mt-4 grid grid-cols-[1fr_auto] gap-x-4 gap-y-2 border-t border-line pt-4 text-xs")[
                            Term("counter tick on a 24 KB page"), Val("24,114 B → ", B.Class("font-mono")["41 B"]),
                            Term("smaller than re-sending the page"), Win("588×"),
                            Term("allocated / update"), Win("~40× less"),
                            Term("bytes that ever leave the server"), Win("just the diff")
                        ]
                    ]
                ]
            ]
        ];

    // A legend swatch. The colour is an inline style because it is READ FROM THE SAME TOKEN the canvas
    // paints with — keeping them one value is the point, and a utility class would fork it.
    private static Component Dot(string color) =>
        Span.Class("size-2.5 shrink-0 rounded-full").Style($"background:{color}");

    // Term, not Key: Key is Component's reconciliation identity, and a helper of that name hides it.
    private static Component Term(string text) => Dt.Class("text-slate-500 dark:text-slate-400")[text];

    private static Component Val(params Component?[] body) => Dd.Class("m-0 text-right text-ink")[body];

    private static Component Win(string text) =>
        Dd.Class("m-0 text-right font-semibold text-signal")[text];

    // .reveal is a JS hook, not styling: App.ts observes it and adds .in. It rides alongside the
    // utilities on every block that should fade up on scroll.
    private static Component SecHead(string eyebrow, string heading, params Component?[] body) =>
        Div.Class("reveal mb-12 max-w-3xl")[
            P.Class(Eyebrow)[eyebrow],
            H2.Class(H2Class)[heading],
            body.Length == 0 ? null : P.Class(Lede)[body]
        ];

    // ---- "one C# class" demo ----
    private Component CounterSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("A component is a class that returns a tree",
                    "Routing, state, and events — one C# class.",
                    "No template dialect, no code-behind, no build step for markup. Markup is a chain — name a component and dot onto it, ", Code["Div.Class(\"card\")[Span[\"hi\"]]"], " — so the IDE lists every step as you type it. Plain, refactor-safe, IDE-native C#. Here's a complete, routable, interactive component:"),
                Div.Class("reveal grid items-start gap-6 lg:grid-cols-2")[
                    Div.Class($"{Card} overflow-hidden")[
                        Div.Class("flex items-center gap-2 border-b border-line bg-panel-2 px-4 py-2.5")[
                            Dot("#ff5f57"), Dot("#febc2e"), Dot("#28c840"),
                            Span.Class("ml-2 font-mono text-xs text-slate-500 dark:text-slate-400")["Counter.cs"]
                        ],
                        Pre.Class("overflow-x-auto p-4 text-xs leading-relaxed")[
                            Code.Class("font-mono")[Raw.Value(CounterCodeHtml)]
                        ]
                    ],
                    // The live tile is a real stateful Rask component — the page proving its own thesis.
                    LiveCounter
                ]
            ]
        ];

    // ---- bytes / benchmarks ----
    // .bar and data-h are the JS contract: App.ts sets each bar's height from data-h once .bars gets
    // .run, so the growth is an animation rather than a layout.
    private Component Bar(string kind, int h, string cap) =>
        Div.Class("bar " + kind).Data(Attr("h", h.ToString()))[Span.Class("cap")[cap]];

    private Component BarCol(string blazorCap, int blazorH, string raskCap, int raskH, string mult, string label) =>
        Div.Class("flex flex-col items-center gap-3")[
            Div.Class("flex h-[260px] items-end gap-2")[
                Bar("blazor", blazorH, blazorCap), Bar("rask", raskH, raskCap)
            ],
            Span.Class("text-center text-xs text-muted [&>b]:mr-1 [&>b]:text-signal")[B[mult], label]
        ];

    private static Component Stat(string kpi, string lab, string sub) =>
        Div.Class($"{Card} p-5")[
            Div.Class("text-2xl font-semibold tabular-nums tracking-tight text-ink")[kpi],
            Div.Class("mt-1 text-sm text-ink-soft")[lab],
            Div.Class("mt-1 text-xs text-slate-500 dark:text-slate-400")[sub]
        ];

    private Component BytesSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Rask vs Blazor · CI-enforced baselines",
                    "Fewer bytes than Blazor — on every scenario.",
                    "Rask treats the network as the real bottleneck: after first paint, a state change ships a minimal diff. Each pair is the ", B["same"], " state change — Blazor's payload beside Rask's. The number is how many ", B["× fewer bytes"], " Rask puts on the wire."),
                Div.Class($"reveal {Card} p-6")[
                    Div.Id("bars").Class("bars flex flex-wrap items-end justify-around gap-8")[
                        BarCol("186 B", 240, "41 B", 53, "4.5×", "Counter / 24 KB page"),
                        BarCol("1,722 B", 240, "137 B", 19, "12.6×", "Deep-tree tick"),
                        BarCol("6,522 B", 240, "441 B", 16, "14.8×", "Deep mutation ×200"),
                        BarCol("2,080 B", 240, "37 B", 5, "56×", "Remove 100 rows")
                    ],
                    Div.Class("mt-6 flex flex-wrap justify-center gap-6 border-t border-line pt-4 text-xs text-slate-500 dark:text-slate-400")[
                        Span.Class("flex items-center gap-2")[Dot("var(--color-blazor)"), "Blazor — full payload"],
                        Span.Class("flex items-center gap-2")[Dot("var(--color-accent)"), "Rask — the diff"]
                    ]
                ],
                Div.Class("reveal mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-4")[
                    Stat("~41 B", "Bytes on the wire", "counter on a 24 KB page · vs 186 B"),
                    Stat("~40×", "Less allocated / update", "1,072 B · vs Blazor 42,972 B"),
                    Stat("~30%", "Leaner retained heap", "158 KB · vs 224 KB (200 rows)"),
                    Stat("1.76×", "Faster render hot path", "598 ns · vs 1,052 ns")
                ],
                P.Class($"reveal mt-6 rounded-xl border border-line bg-signal-soft p-4 text-sm text-ink-soft")["Retained heap used to be Blazor's one win — a pure-element page now keeps a compact frame snapshot instead of an object-per-element graph, so ", B["Rask leads on every measured axis."], " Numbers from the CI-enforced ", A
                    .Href("https://github.com/pal-tamas/rask/blob/main/benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md")
                    .Target("_blank")
                    .Rel("noopener")["vs-blazor baselines"], " (Apple M4, .NET 10)."]
            ]
        ];

    // ---- hosts ----
    private static Component Host(string tag, string title, string prev, params Component?[] body) =>
        Div.Class($"{Card} p-6")[
            Span.Class("font-mono text-xs text-accent-ink")[tag],
            H3.Class("mt-2 text-lg font-semibold text-ink")[title],
            P.Class("mt-2 text-sm leading-relaxed text-slate-500 dark:text-slate-400")[body],
            Span.Class("mt-4 block font-mono text-xs text-ink-soft")[prev]
        ];

    private Component HostsSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("One component model · every host",
                    "Write it once. Ship it where you need it.",
                    "The identical C# component runs unchanged across every host — you choose the runtime per project, not per component."),
                Div.Class("reveal grid gap-4 md:grid-cols-3")[
                    Host("Rask.Server", "Server", "AddRask() · UseRask<TApp>()",
                        "ASP.NET host. State lives on the server; a live diff streams to the browser over a WebSocket. Nothing to compile client-side."),
                    Host("Rask.Wasm", "WebAssembly", "WasmHostBuilder.CreateDefault()",
                        "The same component runs fully client-side on the browser's Mono/WASM runtime via JSImport/JSExport. Ships as an installable, offline PWA."),
                    Host("Rask.Wasm.Hosting", "Static host", "AddRaskWasmHosting()",
                        "Serves a published WASM bundle from an ASP.NET host, with the right content types and pre-compressed variants.")
                ]
            ]
        ];

    // ---- the architecture map, one journey at a time ----
    //
    // The hero runs the same diagram unpinned, cycling every journey by itself. Here a reader who has
    // decided which one they care about can hold it still.
    //
    // The picker is the framework demonstrating itself and nothing more elaborate: a private field, a
    // plain delegate on OnClick, and a re-render. The class attribute that changes on the <svg> is the
    // only thing that goes over the wire.
    private static readonly (FlowScene Scene, string Label)[] Journeys =
    [
        (FlowScene.ServerRoundTrip, "Server round trip"),
        (FlowScene.WasmTakeover, "WASM takeover"),
        (FlowScene.Islands, "Islands"),
        (FlowScene.Spa, "SPA lane"),
        (FlowScene.Durable, "After the response"),
    ];

    private Component HowItFitsSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Every battery, from the UI to the database",
                    "How it all fits together.",
                    "One map, five journeys. Pick one and it stays put — the highlight follows that path "
                    + "through the packages it actually touches."),
                Div.Class("reveal")[
                    Div.Class("mb-6 flex flex-wrap gap-2")[
                        Journeys.Select(j =>
                            Button
                                .Key(j.Label)
                                .Type("button")
                                .OnClick(() => _scene = j.Scene)
                                .Class(_scene == j.Scene ? BtnPrimary + " !px-4 !py-2" : BtnGhost + " !px-4 !py-2")
                                [j.Label]).ToList()
                    ],
                    Div.Class($"{Card} flow-pinned overflow-x-auto p-3")[
                        FlowAnimation.Pinned(_scene)
                    ]
                ]
            ]
        ];

    // ---- features ----
    private static Component Feature(string glyph, string title, params Component?[] desc) =>
        Div.Class($"{Card} p-5")[
            Div.Class("flex items-center gap-2 text-sm font-semibold text-ink")[
                Span.Class("text-accent-ink")[glyph], " ", title
            ],
            P.Class("mt-2 text-sm leading-relaxed text-slate-500 dark:text-slate-400")[desc]
        ];

    private Component FeaturesSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Batteries included · all type-safe",
                    "A full framework, generated at compile time.",
                    "Roslyn source generators build each component's chain surface and typed route URLs — trim-safe, reflection-free, and checked by 30+ compile-time diagnostics."),
                Div.Class("reveal grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    Feature("⌁", "Source generators", "A chain surface per component — ", Code["Card.Title(…)"], " — that demands what the component can't do without, plus type-safe ", Code["Routes.*"], " URL builders. Rename a route, break the build — never a dead link."),
                    Feature("◑", "Scoped CSS & TypeScript", "Drop a sibling ", Code["{Component}.css"], "/", Code[".ts"], ". Auto-scoped, no leaks, no class-name discipline — a mismatch is a build error."),
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
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("DB-backed by default · no external services",
                    "One person's whole back end.",
                    "Behind the same C# UI, every stateful pillar rides the app's own SQLite database — no broker, no Redis, no second service to run. Adding one is a package reference, not a new box to operate."),
                Div.Class("reveal grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    Feature("⊞", "A feature slice", "A CQRS + EF Core CRUD slice — entity, validation, list/create/edit pages, and tests — written once in the tutorial and repeated per feature. Small enough to type, so nothing is generated you can't read."),
                    Feature("◷", "Background jobs", "Durable enqueued, delayed, and recurring work on your database, run by a hosted worker — at-least-once, with exponential backoff."),
                    Feature("✉", "Transactional email", "Email queued on the same database and delivered over SMTP off the request thread; bodies are Rask components."),
                    Feature("⤴", "Transactional outbox", "Domain events captured in the same transaction as your data and relayed at-least-once — crash-safe, no message broker."),
                    Feature("⚡", "Cache", "A database-backed ", Code["IDistributedCache"], " plus a typed ", Code["ICache"], " with ", Code["GetOrAddAsync"], " and sliding/absolute expiry."),
                    Feature("⬢", "Production SQLite", "SQLite as the production database — WAL + busy-timeout pragmas (~99k ops/s on a laptop), continuous Litestream backup, scheduled snapshots."),
                    Feature("⬈", "One-command deploy", Code["rask deploy"], " takes a bare VPS to a live HTTPS site — Docker, a non-root deploy user, firewall + SSH hardening, and zero-downtime swaps."),
                    Feature("◎", "Web Push", "Send Web Push from your backend on your own VAPID keys (RFC 8292/8291) — zero external dependencies.")
                ]
            ]
        ];

    // ---- install ----
    private Component InstallSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                Div.Class("reveal mx-auto mb-10 max-w-2xl text-center")[
                    P.Class($"{Eyebrow} justify-center")["Prerequisite · .NET 10 SDK"],
                    H2.Class(H2Class)["Up and running in one command."]
                ],
                Div.Class("reveal")[InstallTabs]
            ]
        ];

    // ---- footer ----
    private Component FooterSection() =>
        Footer.Class("border-t border-line py-20")[
            Div.Class(Wrap)[
                Div.Class("reveal mx-auto max-w-2xl text-center")[
                    H2.Class(H2Class)["The live docs and the playground are the real tour."],
                    P.Class(Lede)["This is just the front door. Click through a full multi-page Rask app in the browser, or write a component live in the playground."],
                    Div.Class("mt-8 flex flex-wrap justify-center gap-3")[
                        A.Class(BtnPrimary).Href("docs/").Target("_blank").Rel("noopener")["▶ Open the live demo"],
                        A
                            .Class(BtnGhost)
                            .Href("https://github.com/pal-tamas/rask")
                            .Target("_blank")
                            .Rel("noopener")["★ Star on GitHub"]
                    ],
                    Div.Class("mt-10 flex flex-wrap justify-center gap-5 text-sm text-muted [&>a]:no-underline hover:[&>a]:text-ink")[
                        A.Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                        A.Href("playground/").Target("_blank").Rel("noopener")["Playground"],
                        A.Href("https://www.nuget.org/packages/Rask.Server").Target("_blank").Rel("noopener")["NuGet"],
                        A.Href("https://github.com/pal-tamas/rask").Target("_blank").Rel("noopener")["GitHub"]
                    ],
                    P.Class("mt-8 text-xs text-slate-500 dark:text-slate-400")[Span.Class("text-accent-ink")["⚡"], " Rask — Norwegian / Danish / Swedish for ", B["fast"], ". Built with .NET 10 · MIT."]
                ]
            ]
        ];

    private const string BrandSvg =
        "<svg viewBox=\"0 0 128 128\" aria-hidden=\"true\"><defs><linearGradient id=\"tb\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#8b5cf6\"/><stop offset=\"1\" stop-color=\"#7c3aed\"/></linearGradient></defs><rect width=\"128\" height=\"128\" rx=\"28\" fill=\"url(#tb)\"/><path d=\"M74 24 L38 66 L58 66 L53 104 L92 58 L70 58 Z\" fill=\"#fff\"/></svg>";

    // Static, trusted syntax-highlighted markup for the Counter.cs sample (global .t-* classes).
    private const string CounterCodeHtml =
        """
        [<span class="t-type">Route</span>(<span class="t-str">"/counter"</span>)]
        <span class="t-key">public sealed partial class</span> <span class="t-type">Counter</span> : <span class="t-type">Component</span>
        {
            <span class="t-key">private int</span> _count;

            <span class="t-key">protected override</span> <span class="t-type">Component</span>? <span class="t-fn">Render</span>() =&gt;
                <span class="t-type">Button</span>.<span class="t-fn">OnClick</span>(() =&gt; _count++)[<span class="t-str">$"Current count: {_count}"</span>];
        }
        """;
}
