using Rask.Core.Routing;
using Rask.Ui;

namespace Rask.Site.Pages;

/// <summary>
/// The marketing page. One route, which is what makes the site prerenderable at all.
/// </summary>
/// <remarks>
/// <para>
/// Mobile-first, themed by the reader, and built from the same kit the operator console is —
/// <see cref="UiIcon" />, <see cref="UiBadge" />, <see cref="UiSteps" /> and <see cref="UiAura" /> are
/// the kit's, unchanged. What is NOT taken from the kit is its chrome: a marketing page has no tab bar
/// to put in a <c>UiNav</c> and no breadcrumb to switch, so the sections below are ordinary Tailwind
/// over the kit's palette. Borrowing furniture that does not fit would have been the drift the kit was
/// extracted to stop.
/// </para>
/// <para>
/// It used to say "light". It is not: every colour on this page resolves through a daisyUI theme token,
/// so the page repaints with whichever of the thirty-five themes the reader picked, and the choice
/// follows them from here into the docs.
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
        "mb-3 flex items-center gap-2 text-xs font-semibold uppercase tracking-widest text-primary";

    private const string Lede = "mt-5 text-lg leading-relaxed text-base-content";

    private const string Sub = "mt-4 text-sm leading-relaxed text-base-content/70";

    private const string Card = "rounded-2xl border border-base-300 bg-base-100";

    private const string Badge =
        "inline-flex items-center gap-1.5 rounded-full border border-base-300 bg-base-100 px-3 py-1 "
        + "text-xs text-base-content/70";

    // Btn, not Button: a constant named Button would shadow the Button chain entry inside this markup
    // host, and every <button> on the page would then need qualifying.
    //
    // These are anchors, not UiButtons. The kit's button is a <button> with an OnClick — the right shape
    // for an action, and the wrong one for "go to the docs", which has to be a real link a browser can
    // open in a new tab and a crawler can follow.
    private const string Btn =
        "inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-5 text-sm font-semibold "
        + "no-underline transition-colors";

    // `text-base-100!` — important, and not a shortcut. These are ANCHORS, and Tailwind's preflight sets
    // `a { color: inherit }` in its base layer; in this document that rule outranks the text-* utilities,
    // so the primary button took its colour from the hero instead of from its own class. It rendered
    // ink-on-ink at a contrast ratio of 1:1 with `text-base-100` sitting right there in the markup, which
    // is a defect Lighthouse found and no reviewer would. The important modifier is what says "this
    // element's own colour, not the one it inherits".
    private const string BtnPrimary = Btn + " bg-base-content text-base-100! hover:bg-base-content/90";

    private const string BtnGhost =
        Btn + " border border-base-300 bg-base-100 text-base-content! hover:border-primary hover:text-primary";

    private const string SectionPad = "py-16 sm:py-24";

    private const string H2Class = "mt-2 text-3xl font-semibold tracking-tight text-base-content sm:text-4xl";

    /// <inheritdoc />
    // The front door. Its title and description are the site's, which App already carries as the
    // fallback for every page — but the canonical and the Open Graph tags are not, and this is the page
    // most likely to be shared: without og:title and og:description a link to rask.sh unfurls as a bare
    // URL. The canonical also settles "/" against "/index.html", which a static host serves as both.
    protected override Component? HeadAssets =>
        PageMeta.For(
            "Rask — the .NET One Person Framework",
            "Rask is the .NET One Person Framework: one developer builds, runs and ships a whole product "
            + "— UI, data, auth, background work and deploy — from one C# codebase on one SQLite-backed "
            + "server. The same components run on Server and WebAssembly.",
            "/");

    protected override Component? Render() =>
    [
        TopBar(),
        Hero(),
        ShapesSection(),
        HostsSection(),
        FeaturesSection(),
        WholeBackEndSection(),
        InstallSection(),
        FooterSection()
    ];

    // ---- top bar ----
    private Component TopBar() =>
        Header.Class("sticky top-0 z-50 border-b border-base-300 bg-base-100/85 backdrop-blur")[
            Div.Class($"{Wrap} flex h-16 items-center justify-between")[
                Span.Class("flex items-center gap-2 text-lg font-semibold tracking-tight text-base-content")[
                    UiIcon.Name(UiIconName.Bolt).Class("size-5 shrink-0 text-primary"), "Rask"
                ],
                Nav.Class("flex items-center gap-1 text-sm sm:gap-2")[
                    // Hidden on a narrow viewport rather than wrapped: the bar is chrome, and links
                    // stacking over two lines push the hero below the fold on a phone.
                    NavItem("Docs", Rask.Site.Features.Routes.GuidesIndexPage(), hideOnPhone: true),
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
                + "min-h-11 items-center gap-1 rounded-lg px-2 text-base-content/70 no-underline "
                + "hover:bg-base-200 hover:text-base-content")
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
                        H1.Class("text-4xl font-semibold leading-[1.1] tracking-tight text-base-content sm:text-5xl")[
                            "Ship a whole product.", Br, "Just you, and ",
                            Span.Class("text-primary")["C#"], "."
                        ],
                        P.Class(Lede)["Build, run, and ship a complete product — the UI, the data, the auth, the background work, and the deploy — from one C# codebase on one server."],
                        P.Class(Sub)["The same components run server-rendered over a WebSocket or fully client-side on WebAssembly — no ", Code[".razor"], ", no JavaScript, no second language. SQLite is the production database; one box runs the whole thing."],
                        Div.Class("mt-8 flex flex-wrap gap-3")[
                            A.Id("cta-docs").Class(BtnPrimary).Href(Rask.Site.Features.Routes.GuidesIndexPage())["Docs"],
                            A
                                .Class(BtnGhost)
                                .Href("https://github.com/pal-tamas/rask")
                                .Target("_blank")
                                .Rel("noopener")["GitHub"]
                        ],
                        Div.Class("mt-8 flex flex-wrap gap-2")[
                            Fact(UiIconName.Cube, B[".NET 10"]),
                            Fact(UiIconName.ShieldOk, "MIT"),
                            Fact(UiIconName.Server, B["Server"], " · WASM"),
                            Fact(UiIconName.Database, B["SQLite"], " · production DB")
                        ]
                    ],
                    // The page proving its own thesis, on first paint: the component's source, and the
                    // component itself, running.
                    Div.Class("flex min-w-0 flex-col gap-4")[CodeWindow(), LiveCounter]
                ]
            ]
        ];

    // The kit's window frame, not a hand-rolled one.
    //
    // This used to draw its own chrome: a bordered div and three dot spans carrying macOS's traffic
    // lights as inline styles (#ff5f57 / #febc2e / #28c840). They were the last fixed colours in the
    // hero, and they stayed those three hues under all thirty-five themes — a detail that reads as an
    // accident on a page where everything else repaints.
    //
    // daisyUI's .mockup-window draws the same three dots from a pseudo-element in currentColor, so the
    // frame is the theme's, the markup is a component instead of a shape, and nothing here has an
    // opinion about what colour a window is.
    private Component CodeWindow() =>
        UiMockupWindow.Class("overflow-hidden bg-base-100")[
            Div.Class("flex items-center gap-2 border-b border-base-300 bg-base-200 px-4 py-2.5")[
                UiIcon.Name(UiIconName.Document).Class("size-3.5 shrink-0 text-base-content/60"),
                Span.Class("font-mono text-xs text-base-content/70")["Counter.cs"]
            ],
            Pre.Class("overflow-x-auto p-4 text-xs leading-relaxed")[
                Code.Class("font-mono")[Raw.Value(CounterCodeHtml)]
            ]
        ];

    private static Component Fact(UiIconName icon, params Component?[] body) =>
        Span.Class(Badge)[
            UiIcon.Name(icon).Class("size-3.5 shrink-0 text-primary"),
            body
        ];

    private static Component SecHead(string eyebrow, string heading, params Component?[] body) =>
        Div.Class("mb-10 max-w-3xl")[
            P.Class(Eyebrow)[eyebrow],
            H2.Class(H2Class)[heading],
            body.Length == 0 ? null : P.Class(Lede)[body]
        ];

    // ---- every shape one app can take ----
    //
    // This replaced a "Rask vs Blazor · CI-enforced baselines" section: four metric tiles and a table
    // of payload sizes, Blazor's column beside ours. It went for two reasons.
    //
    // It argued the wrong thing. A byte count says this framework beats one other framework at one
    // measurement; it says nothing about what a person can BUILD, which is the only question a reader
    // on the front page is actually asking. And it made the page's central claim depend on a rival
    // staying still — the table needed re-measuring every time either project moved, and a stale
    // number on a marketing page is worse than no number.
    //
    // What replaces it is the shape of the thing: the ladder every page climbs on its own, and the
    // five forms an app can take on top of it. Built from the kit (UiAura, UiSteps, UiStep, UiBadge,
    // UiIcon, UiGrid) and coloured only in theme tokens, so it repaints with the reader's theme like
    // everything else on the page.
    private Component ShapesSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Render modes · islands · SPA · meta front ends · batteries",
                    "One codebase. Every shape a page needs.",
                    "None of this is a mode you put the app into. A page climbs exactly as far as its own render asks for, and every shape below composes on the same component model — chosen ", B["per page"], ", not per project."),

                // The one idea the rest of the page rests on, and the only thing on it that glows.
                // UiAura's own doc says to use it on a single element for exactly this reason.
                UiAura.Style(UiAuraStyle.Glow).Size(UiSize.Lg).Class("mb-10 block rounded-3xl")[
                    Div.Class("rounded-3xl border border-base-300 bg-base-100 p-6 sm:p-8")[
                        Div.Class("mb-6 flex flex-wrap items-center gap-2")[
                            UiIcon.Name(UiIconName.ArrowsUpDown).Class("size-5 shrink-0 text-primary"),
                            Span.Class("text-sm font-semibold text-base-content")["The render ladder"],
                            UiBadge.Label("decided, not declared").Tone("info")
                        ],
                        UiSteps.Class("w-full")[
                            UiStep.Key("static").Text("Static document").Tone(UiTone.Success),
                            UiStep.Key("prerendered").Text("Prerendered HTML").Tone(UiTone.Success),
                            UiStep.Key("live").Text("Live session").Tone(UiTone.Primary)
                        ],
                        P.Class("mt-6 text-sm leading-relaxed text-base-content/70")[
                            "A handler, a form, an element ", Code["Ref"], ", a call into JavaScript, async work still in flight — any one of them and the page keeps a live connection. None of them, and the same component ships as a plain document. ",
                            Code["[RenderMode]"], " overrides the answer where detection cannot see it (a component that pushes from a timer does nothing during the walk), and ",
                            Code["RenderModes"], " is the app-wide ceiling a page can never climb past."
                        ]
                    ]
                ],

                UiGrid[
                    Shape(UiIconName.Sparkles, "per page", "render-modes",
                        "Render modes",
                        "Auto by default: the render decides. Static where a page is a document, interactive where it isn't — and a contradiction is reported rather than silently breaking your buttons."),
                    Shape(UiIconName.Puzzle, "seven runtimes", "islands",
                        "Islands",
                        "React, Preact, Solid, Vue, Svelte, Angular or Lit as an ordinary Rask component — props declared in C#, callbacks re-entering C#. Its subtree is a diff boundary; the rest of the page is still yours."),
                    Shape(UiIconName.Globe, "client-side", "spa",
                        "SPA on WebAssembly",
                        "The identical component tree running fully in the browser on Mono/WASM, routing client-side, installable and offline as a PWA — with every route prerendered to real HTML at publish."),
                    Shape(UiIconName.Stack, "Node, supervised", "meta",
                        "Meta front ends",
                        "Nuxt, Next, SvelteKit, Start, SolidStart or Analog served beside your C# from one container. Rask.Meta.Hosting builds the front end, supervises Node and proxies to it."),
                    Shape(UiIconName.Server, "on by default", "one-person-framework",
                        "Batteries",
                        "Auth, data, background jobs, email, outbox, cache and an operator console — all on the app's own SQLite file. No broker, no Redis, no second box to run."),
                    Shape(UiIconName.Bolt, "compile time", "building-components",
                        "Generated, not reflected",
                        "Roslyn builds each component's chain surface and typed route URLs. Trim-safe, reflection-free, and held by 60+ compile-time diagnostics — rename a route and the build breaks, never a link.")
                ]
            ]
        ];

    // A shape tile, which is also the way into the guide about it. An anchor rather than a UiCard for
    // the same reason the buttons above are anchors: this has to be a real link a browser can open in
    // a new tab and a crawler can follow, and UiCard renders a div.
    private static Component Shape(
        UiIconName icon, string badge, string guide, string title, string body) =>
        A
            .Key(title)
            .Class(
                "group flex flex-col rounded-2xl border border-base-300 bg-base-100 p-6 no-underline "
                + "transition-colors hover:border-primary/40 hover:bg-base-200")
            .Href(GuideHref(guide))
            .Target("_blank")
            .Rel("noopener")[
            Div.Class("flex items-center gap-2")[
                UiIcon.Name(icon).Class("size-5 shrink-0 text-primary"),
                UiBadge.Label(badge),
                UiIcon
                    .Name(UiIconName.ChevronRight)
                    .Class("ml-auto size-4 shrink-0 text-base-content/60 transition-transform group-hover:translate-x-0.5")
            ],
            H3.Class("mt-3 text-lg font-semibold text-base-content")[title],
            P.Class("mt-2 text-sm leading-relaxed text-base-content/70")[body]
        ];

    // ---- hosts ----
    private static Component Host(
        UiIconName icon, string tag, string title, string guide, string prev, params Component?[] body) =>
        A
            .Class(
                $"{Card} guide-link group flex flex-col p-6 no-underline transition-colors "
                + "hover:border-primary/40 hover:bg-base-200")
            .Href(GuideHref(guide))
            .Target("_blank")
            .Rel("noopener")[
            Div.Class("flex items-center gap-2")[
                UiIcon.Name(icon).Class("size-5 shrink-0 text-primary"),
                Span.Class("font-mono text-xs text-base-content/70")[tag],
                UiIcon
                    .Name(UiIconName.ChevronRight)
                    .Class("ml-auto size-4 shrink-0 text-base-content/70 transition-transform group-hover:translate-x-0.5")
            ],
            H3.Class("mt-2 text-lg font-semibold text-base-content")[title],
            P.Class("mt-2 text-sm leading-relaxed text-base-content/70")[body],
            Span.Class("mt-4 block font-mono text-xs text-base-content")[prev]
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
                + "hover:border-primary/40 hover:bg-base-200")
            .Href(GuideHref(guide))
            .Target("_blank")
            .Rel("noopener")[
            Div.Class("flex items-center gap-2 text-sm font-semibold text-base-content")[
                UiIcon.Name(icon).Class("size-4 shrink-0 text-primary"),
                title,
                UiIcon
                    .Name(UiIconName.ChevronRight)
                    .Class("ml-auto size-4 shrink-0 text-base-content/70 transition-transform group-hover:translate-x-0.5")
            ],
            P.Class("mt-2 text-sm leading-relaxed text-base-content/70")[desc]
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
                    Feature(UiIconName.Lock, "Auth, on by default", "authentication", "Register, sign in and sign out with no auth code — a cookie session on both Server and WASM, route guards, and the first account to register becomes the admin. Identity, Keycloak, Auth0, OIDC."),
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
        Footer.Class("border-t border-base-300 py-16 sm:py-20")[
            Div.Class(Wrap)[
                Div.Class("mx-auto max-w-2xl text-center")[
                    H2.Class(H2Class)["The live docs are the real tour."],
                    P.Class(Lede)["This is just the front door. Click through a full multi-page Rask app, running in the browser, and read every guide beside the component it describes."],
                    Div.Class("mt-8 flex flex-wrap justify-center gap-3")[
                        // "Docs", not "Open the live demo". The hero's CTA was renamed when calling the
                        // docs "the live demo" left the docs themselves with no name; this one was
                        // missed, so the same page called the same destination two different things.
                        A.Class(BtnPrimary).Href(Rask.Site.Features.Routes.GuidesIndexPage())["Docs"],
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
                    Div.Class("mt-10 flex flex-wrap justify-center gap-6 text-sm text-base-content/70 "
                              + "[&>a]:no-underline [&>a]:inline-flex [&>a]:min-h-11 [&>a]:items-center "
                              + "[&>a]:px-2 hover:[&>a]:text-base-content")[
                        A.Href(Rask.Site.Features.Routes.GuidesIndexPage())["Docs"],
                        A.Href("https://www.nuget.org/packages/Rask.Server").Target("_blank").Rel("noopener")["NuGet"],
                        A.Href("https://github.com/pal-tamas/rask").Target("_blank").Rel("noopener")["GitHub"]
                    ],
                    P.Class("mt-8 text-xs text-base-content/70")["Rask — Norwegian / Danish / Swedish for ", B["fast"], ". Built with .NET 10 · MIT."]
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
        <span class="text-primary">public sealed partial class</span> <span class="text-ui-ok-ink">Counter</span> : <span class="text-ui-ok-ink">Component</span>
        {
            <span class="text-primary">private int</span> _count;

            <span class="text-primary">protected override</span> <span class="text-ui-ok-ink">Component</span>? <span class="text-base-content">Render</span>() =&gt;
                <span class="text-ui-ok-ink">Button</span>.<span class="text-base-content">OnClick</span>(() =&gt; _count++)[<span class="text-amber-700">$"Current count: {_count}"</span>];
        }
        """;
}
