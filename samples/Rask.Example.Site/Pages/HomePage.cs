using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.Example.Site.Pages;

/// <summary>
/// The marketing page. One route, which is what makes the site prerenderable at all.
/// </summary>
/// <remarks>
/// <para>
/// Light, mobile-first, and built from the same kit the operator console is — <see cref="UiMetricRow" />,
/// <see cref="UiDetailList" />, <see cref="UiStatusDot" /> and <see cref="UiIcon" /> are the console's,
/// unchanged. What is NOT taken from the kit is its chrome: a marketing page has no tab bar to put in a
/// <c>UiNav</c> and no breadcrumb to switch, so the sections below are ordinary Tailwind over the kit's
/// palette. Borrowing furniture that does not fit would have been the drift the kit was extracted to stop.
/// </para>
/// <para>
/// The page ships <b>no JavaScript</b>. The scroll reveals, the growing bars and the hero canvas are gone
/// with the module that drove them, and that is a requirement rather than a simplification: the reveals
/// were <c>opacity: 0</c> until an observer said otherwise, so on a prerendered page every section would
/// have arrived invisible to anyone — crawler included — who never ran the script.
/// </para>
/// </remarks>
[Route("/")]
public sealed partial class HomePage : Component
{
    private static Dictionary<string, string?> Attr(string key, string? value) => new() { [key] = value };

    // The page's vocabulary. Constants rather than @apply: @apply moves the decision into a stylesheet
    // Tailwind then has to be told about, which is the coupling this rewrite removed. A constant is read
    // by the compiler, renamed by the IDE, and found by Tailwind's scanner like any other literal.
    private const string Wrap = "mx-auto w-full max-w-[1100px] px-5 sm:px-6";

    private const string Eyebrow =
        "mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-ui-brand-ink";

    private const string Lede = "mt-5 text-lg leading-relaxed text-ui-ink";

    private const string Sub = "mt-4 text-sm leading-relaxed text-ui-muted";

    private const string Card = "rounded-2xl border border-ui-line bg-ui-bg";

    private const string Badge =
        "rounded-full border border-ui-line bg-ui-bg px-3 py-1 text-xs text-ui-muted";

    // Btn, not Button: a constant named Button would shadow the Button chain entry inside this markup
    // host, and every <button> on the page would then need qualifying.
    //
    // These are anchors, not UiButtons. The kit's button is a <button> with an OnClick — the right shape
    // for an action, and the wrong one for "go to the docs", which has to be a real link a browser can
    // open in a new tab and a crawler can follow.
    private const string Btn =
        "inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-5 text-sm font-semibold "
        + "no-underline transition-colors";

    private const string BtnPrimary = Btn + " bg-ui-ink text-ui-bg hover:bg-ui-ink/90";

    private const string BtnGhost =
        Btn + " border border-ui-line bg-ui-bg text-ui-ink hover:border-ui-brand hover:text-ui-brand-ink";

    private const string SectionPad = "py-16 sm:py-24";

    private const string H2Class = "mt-2 text-3xl font-semibold tracking-tight text-ui-ink sm:text-4xl";

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        TopBar(),
        Hero(),
        BytesSection(),
        HostsSection(),
        FeaturesSection(),
        WholeBackEndSection(),
        InstallSection(),
        FooterSection()
    ];

    // ---- top bar ----
    private Component TopBar() =>
        Header.Class("sticky top-0 z-50 border-b border-ui-line bg-ui-bg/85 backdrop-blur")[
            Div.Class($"{Wrap} flex h-16 items-center justify-between")[
                Span.Class("flex items-center gap-2 text-lg font-semibold tracking-tight text-ui-ink")[
                    UiIcon.Name(UiIconName.Bolt).Class("size-5 shrink-0 text-ui-brand-ink"), "Rask"
                ],
                Nav.Class("flex items-center gap-1 text-sm sm:gap-2")[
                    // Hidden on a narrow viewport rather than wrapped: the bar is chrome, and links
                    // stacking over two lines push the hero below the fold on a phone.
                    NavItem("Docs", "docs/", hideOnPhone: true),
                    NavItem("Playground", "playground/", hideOnPhone: true),
                    NavItem("GitHub", "https://github.com/pal-tamas/rask", hideOnPhone: false),

                    // Every theme the kit ships, switched in CSS. It works on this page precisely
                    // because the page ships no JavaScript — daisyUI matches the checked radio itself.
                    UiThemeDropdown.Placement("dropdown-end")
                ]
            ]
        ];

    private static Component NavItem(string label, string href, bool hideOnPhone) =>
        A
            .Class(
                (hideOnPhone ? "hidden sm:inline-flex " : "inline-flex ")
                + "min-h-11 items-center gap-1 rounded-lg px-2 text-ui-muted no-underline "
                + "hover:bg-ui-well hover:text-ui-ink")
            .Href(href)
            .Target("_blank")
            .Rel("noopener")[
            label,
            UiIcon.Name(UiIconName.ExternalLink).Class("size-3.5 shrink-0 opacity-60")
        ];

    // ---- hero ----
    private Component Hero() =>
        Section.Class("pt-14 pb-16 sm:pt-20 sm:pb-24")[
            Div.Class(Wrap)[
                Div.Class("hero-grid grid items-start gap-10 lg:grid-cols-2 lg:gap-14")[
                    Div[
                        P.Class(Eyebrow)["The .NET One Person Framework"],
                        H1.Class("text-4xl font-semibold leading-[1.1] tracking-tight text-ui-ink sm:text-5xl")[
                            "Ship a whole product.", Br, "Just you, and ",
                            Span.Class("text-ui-brand-ink")["C#"], "."
                        ],
                        P.Class(Lede)["Build, run, and ship a complete product — the UI, the data, the auth, the background work, and the deploy — from one C# codebase on one server."],
                        P.Class(Sub)["The same components run server-rendered over a WebSocket or fully client-side on WebAssembly — no ", Code[".razor"], ", no JavaScript, no second language. SQLite is the production database; one box runs the whole thing."],
                        Div.Class("mt-8 flex flex-wrap gap-3")[
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
                    // The page proving its own thesis, on first paint: the component's source, and the
                    // component itself, running.
                    Div.Class("flex flex-col gap-4")[CodeWindow(), LiveCounter]
                ]
            ]
        ];

    private Component CodeWindow() =>
        Div.Class($"{Card} overflow-hidden")[
            Div.Class("flex items-center gap-2 border-b border-ui-line bg-ui-well px-4 py-2.5")[
                Dot("#ff5f57"), Dot("#febc2e"), Dot("#28c840"),
                Span.Class("ml-2 font-mono text-xs text-ui-muted")["Counter.cs"]
            ],
            Pre.Class("overflow-x-auto p-4 text-xs leading-relaxed")[
                Code.Class("font-mono")[Raw.Value(CounterCodeHtml)]
            ]
        ];

    // A window-chrome dot. The colour is an inline style because these three are macOS's traffic lights,
    // not palette entries — putting them in the theme would invite something else to use them.
    private static Component Dot(string color) =>
        Span.Class("size-2.5 shrink-0 rounded-full").Style($"background:{color}");

    private static Component SecHead(string eyebrow, string heading, params Component?[] body) =>
        Div.Class("mb-10 max-w-3xl")[
            P.Class(Eyebrow)[eyebrow],
            H2.Class(H2Class)[heading],
            body.Length == 0 ? null : P.Class(Lede)[body]
        ];

    // ---- bytes / benchmarks ----
    private Component BytesSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Rask vs Blazor · CI-enforced baselines",
                    "Fewer bytes than Blazor — on every scenario.",
                    "Rask treats the network as the real bottleneck: after first paint, a state change ships a minimal diff. Each pair below is the ", B["same"], " state change — Blazor's payload beside Rask's."),

                // The kit's metric row, unchanged from the console. Two columns on a phone, four from sm
                // up, with the hairlines drawn as a lined background rather than per-cell borders.
                Div.Class("mb-6")[
                    UiMetricRow.Columns(4)[
                        UiMetric.Key("wire").Label("Bytes on the wire").Value("~41 B")
                            .Caption("counter on a 24 KB page · vs 186 B"),
                        UiMetric.Key("alloc").Label("Less allocated / update").Value("~40×")
                            .Caption("1,072 B · vs Blazor 42,972 B"),
                        UiMetric.Key("heap").Label("Leaner retained heap").Value("~30%")
                            .Caption("158 KB · vs 224 KB (200 rows)"),
                        UiMetric.Key("render").Label("Faster render hot path").Value("1.76×")
                            .Caption("598 ns · vs 1,052 ns")
                    ]
                ],

                // A table rather than the animated bars this replaced. The bars were drawn by a script
                // that set each one's height from a data- attribute, so with no script they were all
                // zero — a chart of nothing, on the page's central claim.
                Div.Class($"{Card} overflow-hidden")[
                    Div.Class("overflow-x-auto")[
                        Table.Class("w-full text-left text-sm")[
                            Thead.Class("border-b border-ui-line text-xs uppercase tracking-wide text-ui-muted")[
                                Tr[
                                    Th.Class("px-4 py-3 font-medium")["Scenario"],
                                    Th.Class("px-4 py-3 text-right font-medium")["Blazor"],
                                    Th.Class("px-4 py-3 text-right font-medium")["Rask"],
                                    Th.Class("px-4 py-3 text-right font-medium")["Fewer bytes"]
                                ]
                            ],
                            Tbody[
                                ByteRow("Counter on a 24 KB page", "186 B", "41 B", "4.5×"),
                                ByteRow("Deep-tree tick", "1,722 B", "137 B", "12.6×"),
                                ByteRow("Deep mutation ×200", "6,522 B", "441 B", "14.8×"),
                                ByteRow("Remove 100 rows", "2,080 B", "37 B", "56×")
                            ]
                        ]
                    ]
                ],

                P.Class("mt-6 rounded-xl border border-ui-line bg-ui-bg p-4 text-sm text-ui-muted")["Retained heap used to be Blazor's one win — a pure-element page now keeps a compact frame snapshot instead of an object-per-element graph, so ", B.Class("text-ui-ink")["Rask leads on every measured axis."], " Numbers from the CI-enforced ", A
                    .Class("text-ui-brand-ink underline underline-offset-2")
                    .Href("https://github.com/pal-tamas/rask/blob/main/benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md")
                    .Target("_blank")
                    .Rel("noopener")["vs-blazor baselines"], " (Apple M4 Pro, .NET 10.0.5)."]
            ]
        ];

    private static Component ByteRow(string scenario, string blazor, string rask, string win) =>
        Tr.Key(scenario).Class("border-b border-ui-line/60 last:border-0")[
            Td.Class("px-4 py-3 text-ui-ink")[scenario],
            Td.Class("px-4 py-3 text-right font-mono text-xs tabular-nums text-ui-muted")[blazor],
            Td.Class("px-4 py-3 text-right font-mono text-xs font-semibold tabular-nums text-ui-ink")[rask],
            Td.Class("px-4 py-3 text-right text-sm font-semibold text-ui-ok-ink")[win]
        ];

    // ---- hosts ----
    private static Component Host(
        UiIconName icon, string tag, string title, string guide, string prev, params Component?[] body) =>
        A
            .Class(
                $"{Card} guide-link group flex flex-col p-6 no-underline transition-colors "
                + "hover:border-ui-brand/40 hover:bg-ui-well")
            .Href(GuideHref(guide))
            .Target("_blank")
            .Rel("noopener")[
            Div.Class("flex items-center gap-2")[
                UiIcon.Name(icon).Class("size-5 shrink-0 text-ui-brand-ink"),
                Span.Class("font-mono text-xs text-ui-muted")[tag],
                UiIcon
                    .Name(UiIconName.ChevronRight)
                    .Class("ml-auto size-4 shrink-0 text-ui-muted transition-transform group-hover:translate-x-0.5")
            ],
            H3.Class("mt-2 text-lg font-semibold text-ui-ink")[title],
            P.Class("mt-2 text-sm leading-relaxed text-ui-muted")[body],
            Span.Class("mt-4 block font-mono text-xs text-ui-ink")[prev]
        ];

    private Component HostsSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("One component model · every host",
                    "Write it once. Ship it where you need it.",
                    "The identical C# component runs unchanged across every host — you choose the runtime per project, not per component."),
                Div.Class("grid gap-4 md:grid-cols-3")[
                    Host(UiIconName.Server, "Rask.Server", "Server", "render-modes", "AddRask() · UseRask<TApp>()",
                        "ASP.NET host. State lives on the server; a live diff streams to the browser over a WebSocket. Nothing to compile client-side."),
                    Host(UiIconName.Globe, "Rask.Wasm", "WebAssembly", "pwa", "WasmHostBuilder.CreateDefault()",
                        "The same component runs fully client-side on the browser's Mono/WASM runtime via JSImport/JSExport. Ships as an installable, offline PWA."),
                    Host(UiIconName.Storage, "Rask.Wasm.Hosting", "Static host", "deployment", "AddRaskWasmHosting()",
                        "Serves a published WASM bundle from an ASP.NET host, with the right content types and pre-compressed variants.")
                ]
            ]
        ];

    // ---- features ----

    /// <summary>
    /// A feature card, which is also the way into the guide about it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real anchor wrapping the whole card, not a "read more" link in its corner: the card already
    /// names the thing and describes it, so the card IS the link, and a 44px-plus target beats a
    /// six-character one on a phone. It leaves the app, so it is a plain browser navigation.
    /// </para>
    /// <para>
    /// <paramref name="guide" /> is a docs leaf name — the same slug the docs app's <c>/guides/{slug}</c>
    /// route binds. Every one of them is asserted against the repo's own <c>docs/</c> directory by
    /// <c>GuideLinkTests</c>, because a front door full of 404s is exactly the kind of rot nothing else
    /// would report.
    /// </para>
    /// </remarks>
    private static Component Feature(UiIconName icon, string title, string guide, params Component?[] desc) =>
        A
            .Class(
                $"{Card} guide-link group flex flex-col p-5 no-underline transition-colors "
                + "hover:border-ui-brand/40 hover:bg-ui-well")
            .Href(GuideHref(guide))
            .Target("_blank")
            .Rel("noopener")[
            Div.Class("flex items-center gap-2 text-sm font-semibold text-ui-ink")[
                UiIcon.Name(icon).Class("size-4 shrink-0 text-ui-brand-ink"),
                title,
                UiIcon
                    .Name(UiIconName.ChevronRight)
                    .Class("ml-auto size-4 shrink-0 text-ui-muted transition-transform group-hover:translate-x-0.5")
            ],
            P.Class("mt-2 text-sm leading-relaxed text-ui-muted")[desc]
        ];

    /// <summary>Where a guide lives, relative to this page.</summary>
    /// <remarks>
    /// Document-relative, with no leading slash, exactly like every other asset URL this app emits — the
    /// published <c>&lt;base href&gt;</c> is what decides the prefix, so the same markup is correct at the
    /// origin root and under a sub-path.
    /// </remarks>
    internal static string GuideHref(string guide) => "docs/guides/" + guide;

    private Component FeaturesSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Batteries included · all type-safe",
                    "A full framework, generated at compile time.",
                    "Roslyn source generators build each component's chain surface and typed route URLs — trim-safe, reflection-free, and checked by 60+ compile-time diagnostics."),
                Div.Class("grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    Feature(UiIconName.Bolt, "Source generators", "building-components", "A chain surface per component — ", Code["Card.Title(…)"], " — that demands what the component can't do without, plus type-safe ", Code["Routes.*"], " URL builders. Rename a route, break the build — never a dead link."),
                    Feature(UiIconName.PaintBrush, "Scoped CSS & TypeScript", "js-interop", "Drop a sibling ", Code["{Component}.css"], "/", Code[".ts"], ". Auto-scoped, no leaks, no class-name discipline — a mismatch is a build error. Tailwind v4 compiles from ", Code["dotnet build"], ", with no npm and no config file."),
                    Feature(UiIconName.Clipboard, "Forms & validation", "forms", Code["Form<T>"], " with two-way binding, plus inline, DataAnnotations, FluentValidation, and async validators."),
                    Feature(UiIconName.Lock, "Auth, four ways", "authentication", "Cookie & JWT on both Server and WASM, route guards, and an ", Code["--auth"], " template switch. Identity, Keycloak, Auth0, OIDC."),
                    Feature(UiIconName.ArrowsRightLeft, "CQRS", "cqrs", "Source-generated, trim-safe queries, commands, notifications and pipeline behaviors via ", Code["AddRaskCqrs()"], " — standalone, zero reflection."),
                    Feature(UiIconName.Phone, "PWA & Web Push", "pwa", "Typed manifest, a default service worker, and VAPID/RFC-8291 Web Push with zero external deps. ", Code["--pwa"], " and you're installable."),
                    Feature(UiIconName.Cube, "50 typed browser APIs", "browser-apis", "Storage, clipboard, geolocation, passkeys, share, sensors, observers, serial/USB/HID/Bluetooth — one awaitable C# layer, identical on Server & WASM."),
                    Feature(UiIconName.ShieldOk, "Secure by default", "best-practices", "Strings are HTML-encoded, URL attributes are scheme-sanitized (", Code["javascript:"], " → ", Code["about:blank"], "). Safe output is the default, not a flag."),
                    Feature(UiIconName.Retry, "C# Hot Reload", "getting-started", "Edit ", Code["Render()"], " or scoped css/js under ", Code["dotnet watch"], " and it re-renders live — the closest a compiled framework gets to a no-build loop."),
                    Feature(UiIconName.Puzzle, "Islands", "islands", "React, Preact, Solid, Vue, Svelte, Angular or Lit — any of the seven as an ordinary Rask component, with its props declared in C# and its callbacks re-entering C#. A real Blazor component too — MudBlazor, an RCL — hosted server-rendered."),
                    Feature(UiIconName.Sparkles, "Prerendering & render modes", "prerendering", "A WASM app renders every route to real HTML at publish, so a crawler is served the page rather than a spinner. On the server, ", Code["RenderModes"], " decides per page whether it needs a live session at all."),
                    Feature(UiIconName.Globe, "Meta framework front ends", "meta", "Nuxt, Next, SvelteKit, Start, SolidStart or Analog served beside your C# from one container — ", Code["Rask.Meta.Hosting"], " builds the front end, supervises Node and proxies to it."),
                    Feature(UiIconName.Terminal, "One CLI", "cli", Code["rask new"], ", ", Code["rask dev"], ", ", Code["rask db"], ", ", Code["rask deploy"], " — scaffold, run, migrate and ship without leaving the terminal.")
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
                Div.Class("grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    Feature(UiIconName.Stack, "A feature slice", "cqrs", "A CQRS + EF Core CRUD slice — entity, validation, list/create/edit pages, and tests — written once in the tutorial and repeated per feature. Small enough to type, so nothing is generated you can't read."),
                    Feature(UiIconName.Clock, "Background jobs", "jobs", "Durable enqueued, delayed, and recurring work on your database, run by a hosted worker — at-least-once, with exponential backoff."),
                    Feature(UiIconName.Envelope, "Transactional email", "mail", "Email queued on the same database and delivered over SMTP off the request thread; bodies are Rask components."),
                    Feature(UiIconName.Outbox, "Transactional outbox", "outbox", "Domain events captured in the same transaction as your data and relayed at-least-once — crash-safe, no message broker."),
                    Feature(UiIconName.Bolt, "Cache & query", "cache", "A database-backed ", Code["IDistributedCache"], ", a typed ", Code["ICache"], " with ", Code["GetOrAddAsync"], ", and ", Code["Rask.Query"], " wrapping the dispatcher with dedup, staleness and invalidation."),
                    Feature(UiIconName.Database, "Production SQLite", "sqlite", "SQLite as the production database — WAL + busy-timeout pragmas, continuous Litestream backup, scheduled snapshots. Postgres and SQL Server are a package away."),
                    Feature(UiIconName.Overview, "An operator console", "dashboard", "A dashboard at ", Code["/_rask"], " over every pillar's own table — queue depth, dead letters and the errors behind them, cache contents, a log tail, SQLite pragmas. Fail-closed behind an authorization policy."),
                    Feature(UiIconName.Archive, "Durable logs", "logging", Code["Rask.Logging"], " keeps the ", Code["ILogger"], " pipeline in a SQLite file of its own, buffered off the request thread, with retention by age and row count — and a searchable view in the console."),
                    Feature(UiIconName.Rocket, "One-command deploy", "deployment", Code["rask deploy"], " takes a bare VPS to a live HTTPS site — Docker, a non-root deploy user, firewall + SSH hardening, and zero-downtime swaps."),
                    Feature(UiIconName.Bell, "Web Push", "webpush", "Send Web Push from your backend on your own VAPID keys (RFC 8292/8291) — zero external dependencies."),
                    Feature(UiIconName.Globe, "WebRTC signaling", "browser-apis", Code["Rask.Signaling"], " hosts the relay that ", Code["IWebRtc"], " connects to, so peer-to-peer works without a third-party service."),
                    Feature(UiIconName.Storage, "Object storage", "http-and-files", Code["Rask.ObjectStore"], " puts uploads behind one typed abstraction — the local disk in development, S3-compatible storage in production.")
                ]
            ]
        ];

    // ---- install ----
    private Component InstallSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                Div.Class("mx-auto mb-10 max-w-2xl text-center")[
                    P.Class($"{Eyebrow} justify-center")["Prerequisite · .NET 10 SDK"],
                    H2.Class(H2Class)["Up and running in one command."]
                ],
                InstallTabs
            ]
        ];

    // ---- footer ----
    private Component FooterSection() =>
        Footer.Class("border-t border-ui-line py-16 sm:py-20")[
            Div.Class(Wrap)[
                Div.Class("mx-auto max-w-2xl text-center")[
                    H2.Class(H2Class)["The live docs and the playground are the real tour."],
                    P.Class(Lede)["This is just the front door. Click through a full multi-page Rask app in the browser, or write a component live in the playground."],
                    Div.Class("mt-8 flex flex-wrap justify-center gap-3")[
                        // "Docs", not "Open the live demo". The hero's CTA was renamed when calling the
                        // docs "the live demo" left the docs themselves with no name; this one was
                        // missed, so the same page called the same destination two different things.
                        A.Class(BtnPrimary).Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                        A
                            .Class(BtnGhost)
                            .Href("https://github.com/pal-tamas/rask")
                            .Target("_blank")
                            .Rel("noopener")[
                            UiIcon.Name(UiIconName.Star).Class("size-4 shrink-0"), "Star on GitHub"
                        ]
                    ],
                    Div.Class("mt-10 flex flex-wrap justify-center gap-5 text-sm text-ui-muted [&>a]:no-underline hover:[&>a]:text-ui-ink")[
                        A.Href("docs/").Target("_blank").Rel("noopener")["Docs"],
                        A.Href("playground/").Target("_blank").Rel("noopener")["Playground"],
                        A.Href("https://www.nuget.org/packages/Rask.Server").Target("_blank").Rel("noopener")["NuGet"],
                        A.Href("https://github.com/pal-tamas/rask").Target("_blank").Rel("noopener")["GitHub"]
                    ],
                    P.Class("mt-8 text-xs text-ui-muted")["Rask — Norwegian / Danish / Swedish for ", B["fast"], ". Built with .NET 10 · MIT."]
                ]
            ]
        ];

    // Static, trusted syntax-highlighted markup for the Counter.cs sample.
    //
    // The colours are Tailwind utilities inline rather than the .t-* classes this used to carry: those
    // lived in a hand-written @layer components block, and the block existed only for them once the
    // page's JavaScript went. The C# inside is pinned byte-for-byte against README.md and NUGET.md by
    // scripts/tests/front-doors.test.sh — which strips these tags before comparing, so the classes may
    // change and the code may not.
    private const string CounterCodeHtml =
        """
        [<span class="text-ui-ok-ink">Route</span>(<span class="text-amber-700">"/counter"</span>)]
        <span class="text-ui-brand-ink">public sealed partial class</span> <span class="text-ui-ok-ink">Counter</span> : <span class="text-ui-ok-ink">Component</span>
        {
            <span class="text-ui-brand-ink">private int</span> _count;

            <span class="text-ui-brand-ink">protected override</span> <span class="text-ui-ok-ink">Component</span>? <span class="text-ui-ink">Render</span>() =&gt;
                <span class="text-ui-ok-ink">Button</span>.<span class="text-ui-ink">OnClick</span>(() =&gt; _count++)[<span class="text-amber-700">$"Current count: {_count}"</span>];
        }
        """;
}
