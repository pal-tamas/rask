using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.Site.Pages;

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

    // `text-ui-bg!` — important, and not a shortcut. These are ANCHORS, and Tailwind's preflight sets
    // `a { color: inherit }` in its base layer; in this document that rule outranks the text-* utilities,
    // so the primary button took its colour from the hero instead of from its own class. It rendered
    // ink-on-ink at a contrast ratio of 1:1 with `text-ui-bg` sitting right there in the markup, which
    // is a defect Lighthouse found and no reviewer would. The important modifier is what says "this
    // element's own colour, not the one it inherits".
    private const string BtnPrimary = Btn + " bg-ui-ink text-ui-bg! hover:bg-ui-ink/90";

    private const string BtnGhost =
        Btn + " border border-ui-line bg-ui-bg text-ui-ink! hover:border-ui-brand hover:text-ui-brand-ink";

    private const string SectionPad = "py-16 sm:py-24";

    private const string H2Class = "mt-2 text-3xl font-semibold tracking-tight text-ui-ink sm:text-4xl";

    /// <inheritdoc />
    // The front door. Its title and description are the site's, which App already carries as the
    // fallback for every page — but the canonical and the Open Graph tags are not, and this is the page
    // most likely to be shared: without og:title and og:description a link to rask.sh unfurls as a bare
    // URL. The canonical also settles "/" against "/index.html", which a static host serves as both.
    protected override Component? HeadAssets =>
        PageMeta.For(SiteIdentity.Title, SiteIdentity.Description, "/");

    protected override Component? Render() =>
    [
        TopBar(),
        Hero(),
        BytesSection(),
        HostsSection(),
        FrontEndsSection(),
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
                    NavItem("Docs", Rask.Site.Features.Routes.GuidesIndexPage(), hideOnPhone: true),
                    ExternalNavItem("GitHub", "https://github.com/pal-tamas/rask", hideOnPhone: false),

                    // Every theme the kit ships, switched in CSS — and now REMEMBERED, by the boot
                    // script rather than by this component (see App.ThemeInitJs). It stays the kit's
                    // handler-free picker on purpose: a C# one puts handlers in the chrome of every
                    // page, and handler ids are positional, which silently broke the islands.
                    UiThemeDropdown.Placement("dropdown-end")
                ]
            ]
        ];

    private const string NavItemClass =
        "min-h-11 items-center gap-1 rounded-lg px-2 text-ui-muted no-underline "
        + "hover:bg-ui-well hover:text-ui-ink";

    /// <summary>
    ///     A link to another page OF THIS APP — so it stays in the tab and navigates as an SPA.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         There used to be one NavItem, and it stamped <c>target="_blank"</c> and an external-link
    ///         glyph on everything, "Docs" included. That is what made the front door's own Docs link open
    ///         a SECOND TAB and cold-boot the entire WASM app — several MB of runtime, a boot screen, and
    ///         the hydration reflow all over again.
    ///     </para>
    ///     <para>
    ///         <b>NavLink, not A, and that is the whole difference.</b> The runtime intercepts clicks on
    ///         <c>a[data-rask-nav]</c>, and NavLink is what writes that attribute — a bare
    ///         <c>&lt;a href&gt;</c> is a plain document navigation no matter how internal its URL is, so
    ///         every link on this page reloaded the app from scratch. It also takes a type-safe
    ///         <c>RouteUrl</c>, so a renamed route is a build error rather than a dead link.
    ///         <c>ActiveClass("")</c> opts out of active styling: this is chrome, not a section nav.
    ///     </para>
    /// </remarks>
    private static Component NavItem(string label, RouteUrl href, bool hideOnPhone) =>
        NavLink
            .Href(href)
            .ActiveClass("")
            .Class((hideOnPhone ? "hidden sm:inline-flex " : "inline-flex ") + NavItemClass)[label];

    /// <summary>A link that leaves the site — new tab, and it says so.</summary>
    private static Component ExternalNavItem(string label, string href, bool hideOnPhone) =>
        A
            .Class((hideOnPhone ? "hidden sm:inline-flex " : "inline-flex ") + NavItemClass)
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
                    // min-w-0 on BOTH tracks. A grid item's min-width defaults to `auto`, which
                    // resolves to its min-content size — and the code window's <pre> carries
                    // `white-space: pre`, so its min-content is the longest source line, 510px.
                    // `overflow-x-auto` on the <pre> does not help: that makes the PRE scroll, it
                    // does not shrink the track asking to be 510 wide. The single column on a phone
                    // therefore grew past the viewport and took the whole document with it —
                    // measured at 532px against a 390px screen, which is why the hero text was
                    // rendered small and clipped rather than wrapped.
                    Div.Class("min-w-0")[
                        P.Class(Eyebrow)["The .NET One Person Framework"],
                        H1.Class("text-4xl font-semibold leading-[1.1] tracking-tight text-ui-ink sm:text-5xl")[
                            "Ship a whole product.", Br, "Just you, and ",
                            Span.Class("text-ui-brand-ink")["C#"], "."
                        ],
                        P.Class(Lede)["Build, run, and ship a complete product — the UI, the data, the auth, the background work, and the deploy — from one C# codebase on one server."],
                        P.Class(Sub)["The same components run server-rendered over a WebSocket or fully client-side on WebAssembly — no ", Code[".razor"], ", no JavaScript, no second language. SQLite is the production database; one box runs the whole thing."],
                        Div.Class("mt-8 flex flex-wrap gap-3")[
                            NavLink
                                .Href(Rask.Site.Features.Routes.GuidesIndexPage())
                                .Id("cta-docs")
                                .ActiveClass("")
                                .Class(BtnPrimary)["Docs"],
                            A
                                .Class(BtnGhost)
                                .Href("https://github.com/pal-tamas/rask")
                                .Target("_blank")
                                .Rel("noopener")["GitHub"]
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
                    Div.Class("flex min-w-0 flex-col gap-4")[CodeWindow(), LiveCounter]
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
                    .Href("https://github.com/pal-tamas/rask/blob/main/tests/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md")
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

    // ---- hosts & front-end lanes ----
    private static Component LaneCard(
        UiIconName icon, string tag, string title, string guide, string prev, params Component?[] body) =>
        NavLink
            .Href(Rask.Site.Features.Routes.GuidePage(guide))
            .ActiveClass("")
            .Class(
                $"{Card} guide-link group flex flex-col p-6 no-underline transition-colors "
                + "hover:border-ui-brand/40 hover:bg-ui-well")[
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
                    LaneCard(UiIconName.Server, "Rask.Server", "Server", "render-modes", "AddRask() · UseRask<TApp>()",
                        "ASP.NET host. State lives on the server; a live diff streams to the browser over a WebSocket. Nothing to compile client-side."),
                    LaneCard(UiIconName.Globe, "Rask.Wasm", "WebAssembly", "pwa", "WasmHostBuilder.CreateDefault()",
                        "The same component runs fully client-side on the browser's Mono/WASM runtime via JSImport/JSExport. Ships as an installable, offline PWA."),
                    LaneCard(UiIconName.Storage, "Rask.Wasm.Hosting", "Static host", "deployment", "AddRaskWasmHosting()",
                        "Serves a published WASM bundle from an ASP.NET host, with the right content types and pre-compressed variants.")
                ]
            ]
        ];

    // ---- front ends ----
    /// <summary>
    ///     The front-ends section's DOM id — the handle its tests address it by.
    /// </summary>
    /// <remarks>
    /// The heading counts the lanes in words ("Four front ends"), and a count written in prose is the
    /// kind that goes stale silently — the README's equivalent section said "Three" for as long as
    /// there were four lanes to choose between. <c>FrontEndsTests</c> slices the page at this id and
    /// counts the cards inside it, so adding a lane without rewording the heading fails rather than
    /// merely reads wrong.
    /// </remarks>
    internal const string FrontEndsSectionId = "front-ends";

    private Component FrontEndsSection() =>
        Section.Id(FrontEndsSectionId).Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Four front ends · one back end",
                    "Bring your own front end — or don't.",
                    "Every lane answers to the same C# back end over the same typed wire. Pick one per project; islands also compose inside a Rask component tree, so those two mix freely."),
                Div.Class("grid gap-4 md:grid-cols-2")[
                    LaneCard(UiIconName.CodeBracket, "Rask.Core", "Rask components", "render-modes", "rask new Shop",
                        "C# components server-rendered over a WebSocket, every state change streaming as a minimal diff. Add ", Code["--wasm"], " and the same components also publish as a WebAssembly bundle out of the same project."),
                    LaneCard(UiIconName.Puzzle, "Rask.External", "Islands", "islands", "class Chart : ReactComponent",
                        "A ", Code[".tsx"], ", ", Code[".vue"], ", ", Code[".svelte"], " or Lit file as an ordinary Rask component — props declared in C#, callbacks re-entering C#, and the live diff leaving the subtree to its own renderer. A real Blazor component too."),
                    LaneCard(UiIconName.Desktop, "Rask.Spa.Hosting", "TypeScript SPA", "spa", "rask new Shop --template react",
                        "A TypeScript single-page app on an ASP.NET host — seven frameworks, with the client's types generated from your C# message records on every build. No Node at runtime."),
                    LaneCard(UiIconName.Globe, "Rask.Meta.Hosting", "Meta framework", "meta", "rask new Shop --template nuxt",
                        "Nuxt, Next, SvelteKit, TanStack Start, SolidStart or Analog owning the whole front end, with Rask the backend behind it. One container, one port: Kestrel fronts every request and supervises Node on loopback.")
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
        NavLink
            .Href(Rask.Site.Features.Routes.GuidePage(guide))
            .ActiveClass("")
            .Class(
                $"{Card} guide-link group flex flex-col p-5 no-underline transition-colors "
                + "hover:border-ui-brand/40 hover:bg-ui-well")[
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
                    Feature(UiIconName.Lock, "Auth, on by default", "authentication", "Register, sign in and sign out with no auth code — a cookie session on both Server and WASM, route guards, and the first account to register becomes the admin. Identity, Keycloak, Auth0, OIDC."),
                    Feature(UiIconName.ArrowsRightLeft, "CQRS", "cqrs", "Source-generated, trim-safe queries, commands, notifications and pipeline behaviors via ", Code["AddRaskCqrs()"], " — standalone, zero reflection."),
                    Feature(UiIconName.Phone, "PWA & Web Push", "pwa", "Typed manifest, a default service worker, and VAPID/RFC-8291 Web Push with zero external deps. ", Code["--pwa"], " and you're installable."),
                    Feature(UiIconName.Cube, "50 typed browser APIs", "browser-apis", "Storage, clipboard, geolocation, passkeys, share, sensors, observers, serial/USB/HID/Bluetooth — one awaitable C# layer, identical on Server & WASM."),
                    Feature(UiIconName.ShieldOk, "Secure by default", "best-practices", "Strings are HTML-encoded, URL attributes are scheme-sanitized (", Code["javascript:"], " → ", Code["about:blank"], "). Safe output is the default, not a flag."),
                    Feature(UiIconName.Retry, "C# Hot Reload", "getting-started", "Edit ", Code["Render()"], " or scoped css/js under ", Code["dotnet watch"], " and it re-renders live — the closest a compiled framework gets to a no-build loop."),
                    Feature(UiIconName.Sparkles, "Prerendering & render modes", "prerendering", "A WASM app renders every route to real HTML at publish, so a crawler is served the page rather than a spinner. On the server, ", Code["RenderModes"], " decides per page whether it needs a live session at all."),
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
                    Feature(UiIconName.Storage, "File storage", "file-storage", Code["Rask.Storage"], " keeps uploads on disk, in S3-compatible storage or in Azure Blob, with a row per file on your database — public or expiring links, and the content type sniffed from the bytes rather than taken from the browser.")
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
                    H2.Class(H2Class)["The live docs are the real tour."],
                    P.Class(Lede)["This is just the front door. Click through a full multi-page Rask app, running in the browser, and read every guide beside the component it describes."],
                    Div.Class("mt-8 flex flex-wrap justify-center gap-3")[
                        // "Docs", not "Open the live demo". The hero's CTA was renamed when calling the
                        // docs "the live demo" left the docs themselves with no name; this one was
                        // missed, so the same page called the same destination two different things.
                        NavLink.Href(Rask.Site.Features.Routes.GuidesIndexPage()).ActiveClass("").Class(BtnPrimary)["Docs"],
                        A
                            .Class(BtnGhost)
                            .Href("https://github.com/pal-tamas/rask")
                            .Target("_blank")
                            .Rel("noopener")[
                            UiIcon.Name(UiIconName.Star).Class("size-4 shrink-0"), "Star on GitHub"
                        ]
                    ],
                    // min-h-11 and gap-6 on the links, not just on the row. A 14px line of text is a
                    // 20px tap target, under the 24px WCAG 2.2 minimum and well under the 44px a thumb
                    // actually needs — and three of them 20px apart is the shape that makes a phone user
                    // hit "GitHub" when they meant "NuGet". Padding is the fix rather than a bigger font:
                    // the target grows, the type stays as designed.
                    Div.Class("mt-10 flex flex-wrap justify-center gap-6 text-sm text-ui-muted "
                              + "[&>a]:no-underline [&>a]:inline-flex [&>a]:min-h-11 [&>a]:items-center "
                              + "[&>a]:px-2 hover:[&>a]:text-ui-ink")[
                        NavLink.Href(Rask.Site.Features.Routes.GuidesIndexPage()).ActiveClass("")["Docs"],
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
