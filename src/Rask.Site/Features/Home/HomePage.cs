using Rask.Core.Routing;

namespace Rask.Site.Pages;

/// <summary>
/// The marketing page. One route, which is what makes the site prerenderable at all.
/// </summary>
/// <remarks>
/// <para>
/// Light, mobile-first, and built from the same kit the operator console is — <see cref="UiIcon" /> and
/// the theme dropdown are the console's, unchanged. What is NOT taken from the kit is its chrome: a
/// marketing page has no tab bar to put in a <c>UiNav</c> and no breadcrumb to switch, so the sections
/// below are ordinary Tailwind over the kit's
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
    //
    // The column itself moved to SiteLayout when the top bar became SiteHeader: the bar centres in the
    // same column these sections do, and two files spelling out one width is how the two drift apart.
    private const string Wrap = SiteLayout.Wrap;

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
        // The shared bar, centred in this page's own column. It used to be written out here; /docs wore
        // a different one, and "different" had grown to a daisyUI navbar, a second brand mark, two
        // badges, a route readout and a bordered button.
        SiteHeader.FullBleed(false),
        Hero(),
        // Back end first. Rask is the whole stack, and the stack is what a visitor cannot get from a UI
        // library; the components come second, then where they run, then which front end to put on it.
        WholeStackSection(),
        FrontendSection(),
        HostsSection(),
        FrontEndsSection(),
        InstallSection(),
        FooterSection()
    ];

    // ---- hero ----
    private Component Hero() =>
        Section.Class("pt-14 pb-16 sm:pt-20 sm:pb-24")[
            Div.Class(Wrap)[
                // The code track is SIZED FOR ITS CODE, not half the row. At an even split the code
                // window was 496px against a 510px line in JetBrains Mono (525px in the fallback a cold
                // load paints first), so at every two-column width the <pre> was a live horizontal
                // scroller over 14px of nothing — and Safari shows an overlay scrollbar late, when a
                // scroller's content size changes under it (the font swapping in). 34rem clears the
                // fallback with room to spare, and HeroCode keeps its lines short enough to stay inside.
                Div.Class("hero-grid grid items-start gap-10 lg:grid-cols-[minmax(0,1fr)_minmax(0,34rem)] lg:gap-14")[
                    // min-w-0 on BOTH tracks. A grid item's min-width defaults to `auto`, which
                    // resolves to its min-content size — and the code window's <pre> carries
                    // `white-space: pre`, so its min-content is the longest source line, 510px.
                    // `overflow-x-auto` on the <pre> does not help: that makes the PRE scroll, it
                    // does not shrink the track asking to be 510 wide. The single column on a phone
                    // therefore grew past the viewport and took the whole document with it —
                    // measured at 532px against a 390px screen, which is why the hero text was
                    // rendered small and clipped rather than wrapped.
                    Div.Class("min-w-0")[
                        P.Class(Eyebrow)["The full-stack .NET web framework"],
                        // 2.75rem beside the code: the text track is 452px there, and a longer first line
                        // would break the headline's two designed lines into three.
                        H1.Class("text-4xl font-semibold leading-[1.1] tracking-tight text-ui-ink sm:text-5xl lg:text-[2.75rem]")[
                            "The whole stack,", Br, "in ",
                            Span.Class("text-ui-brand-ink")["C#"], "."
                        ],
                        P.Class(Lede)["Data, queries, auth, background jobs, email, realtime and deploy — one framework and one C# codebase, for a team of one or fifty."],
                        P.Class(Sub)["Declare an aggregate, query it from a page, ship it with ", Code["rask deploy"], ". The UI is C# components, live over a WebSocket or running in WebAssembly — or bring React, Vue, Angular or Nuxt to the same back end."],
                        Div.Class("mt-8 flex flex-wrap gap-3")[
                            NavLink
                                .Href(PageMeta.LinkTo(Rask.Site.Features.Routes.GuidesIndexPage()))
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
                            Span.Class(Badge)[B[".NET 10"], " · 11"],
                            Span.Class(Badge)["MIT"],
                            Span.Class(Badge)[B["Server"], " · WASM"],
                            Span.Class(Badge)[B["SQLite"], " · Postgres · SQL Server"]
                        ]
                    ],
                    // One feature back to front, on first paint: the aggregate that is the table, and the
                    // page that queries it. The counter that used to sit here leads the Frontend section.
                    Div.Class("flex min-w-0 flex-col gap-4")[HeroCode]
                ]
            ]
        ];

    private static Component CodeWindow() =>
        Div.Class($"{Card} min-w-0 overflow-hidden")[
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

    // ---- hosts & front-end lanes ----
    private static Component LaneCard(
        Ui.IconName icon, string tag, string title, string guide, string prev, params Component?[] body) =>
        NavLink
            .Href(PageMeta.LinkTo(Rask.Site.Features.Routes.GuidePage(guide)))
            .ActiveClass("")
            .Class(
                $"{Card} guide-link group flex flex-col p-6 no-underline transition-colors "
                + "hover:border-ui-brand/40 hover:bg-ui-well")[
            Div.Class("flex items-center gap-2")[
                Ui.Icon.Name(icon).Class("size-5 shrink-0 text-ui-brand-ink"),
                Span.Class("font-mono text-xs text-ui-muted")[tag],
                Ui.Icon
                    .Name(Ui.IconName.ChevronRight)
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
                    LaneCard(Ui.IconName.Server, "Rask.Server", "Server", "render-modes", "AddRask() · MapRask<TApp>()",
                        "ASP.NET host. State lives on the server; a live diff streams to the browser over a WebSocket. Nothing to compile client-side."),
                    LaneCard(Ui.IconName.Globe, "Rask.Wasm", "WebAssembly", "pwa", "WasmHostBuilder.CreateDefault()",
                        "The same component runs fully client-side on the browser's Mono/WASM runtime via JSImport/JSExport. Ships as an installable, offline PWA."),
                    LaneCard(Ui.IconName.Storage, "Rask.Spa.Hosting", "Single-page host", "spa", "AddRaskSpaHost() · MapRaskSpa()",
                        "Serves a WebAssembly app or a TypeScript bundle from an ASP.NET host, cached by what its build guarantees, with pre-compressed variants.")
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
                    "Rask is a superset, not a rival: React, Vue, Svelte, Angular and Lit components, a real Blazor component, a TypeScript SPA or a Nuxt or Next.js app all run on it, against the same C# back end over the same typed wire. Pick one per project; islands also compose inside a Rask component tree, so those two mix freely."),
                Div.Class("grid gap-4 md:grid-cols-2")[
                    LaneCard(Ui.IconName.CodeBracket, "Rask.Core", "Rask components", "render-modes", "rask new Shop",
                        "C# components server-rendered over a WebSocket, every state change streaming as a minimal diff. Pick ", Code["-t wasm-hosted"], " and the same components publish as a WebAssembly bundle the host serves, out of the same project."),
                    LaneCard(Ui.IconName.Puzzle, "Rask.External", "Islands", "islands", "class Chart : ReactComponent",
                        "A ", Code[".tsx"], ", ", Code[".vue"], ", ", Code[".svelte"], " or Lit file as an ordinary Rask component — props declared in C#, callbacks re-entering C#, and the live diff leaving the subtree to its own renderer. A real Blazor component too."),
                    LaneCard(Ui.IconName.Desktop, "Rask.Spa.Hosting", "TypeScript SPA", "spa", "rask new Shop --template react",
                        "A TypeScript single-page app on an ASP.NET host — seven frameworks, with the client's types generated from your C# message records on every build. No Node at runtime."),
                    LaneCard(Ui.IconName.Globe, "Rask.Meta.Hosting", "Meta framework", "meta", "rask new Shop --template nuxt",
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
    private static Component Feature(Ui.IconName icon, string title, string guide, params Component?[] desc) =>
        NavLink
            .Href(PageMeta.LinkTo(Rask.Site.Features.Routes.GuidePage(guide)))
            .ActiveClass("")
            .Class(
                $"{Card} guide-link group flex flex-col p-5 no-underline transition-colors "
                + "hover:border-ui-brand/40 hover:bg-ui-well")[
            Div.Class("flex items-center gap-2 text-sm font-semibold text-ui-ink")[
                Ui.Icon.Name(icon).Class("size-4 shrink-0 text-ui-brand-ink"),
                title,
                Ui.Icon
                    .Name(Ui.IconName.ChevronRight)
                    .Class("ml-auto size-4 shrink-0 text-ui-muted transition-transform group-hover:translate-x-0.5")
            ],
            P.Class("mt-2 text-sm leading-relaxed text-ui-muted")[desc]
        ];

    /// <summary>
    ///     The whole-stack section's DOM id — the handle its tests address it by.
    /// </summary>
    /// <remarks>
    /// It is the first section under the hero, and it is the back end: <c>HomePageTests</c> and the site
    /// journey assert that it still is, and that it comes before the front end, so a section slotted in
    /// above it has to mean to push the stack down.
    /// </remarks>
    internal const string WholeStackSectionId = "whole-stack";

    /// <summary>The frontend section's DOM id — the handle its tests address it by.</summary>
    internal const string FrontendSectionId = "frontend";

    // ---- the whole stack (back end first) ----
    //
    // One card per shipped piece, each the way into its guide. No card for anything that is not in the
    // box: a front door that promises a package nobody can reference is worse than a shorter list.
    private Component WholeStackSection() =>
        Section.Id(WholeStackSectionId).Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("The whole stack · one codebase",
                    "Everything behind the page, already built.",
                    "Data, queries, auth, jobs, email, cache, files, realtime, tenants and search ship with the framework and ride your app's own database — no broker to stand up, no Redis to run, and a Redis you already have plugs in as the cache. They are built on the standard .NET pieces — EF Core, hosted services, ILogger, IDistributedCache — so each is a package reference, not a new box to operate."),
                Div.Class("grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    Feature(Ui.IconName.Database, "Data & aggregates", "data", "Derive from ", Code["Aggregate<Guid>"], " and the class is the table — key, timestamps and a concurrency version included, plus a generated read face to query. EF Core on SQLite, PostgreSQL or SQL Server."),
                    Feature(Ui.IconName.ArrowsRightLeft, "Queries & commands", "query", Code["QueryClient.Query(…)"], " in ", Code["Render"], " — cached, deduplicated, refetched after a write. Source-generated CQRS underneath, and the same call from the browser or the server."),
                    Feature(Ui.IconName.Lock, "Auth, on by default", "authentication", "Register, sign in, passkeys and a session row per device with no auth code — sign-in pages scaffolded into your app, the first account made admin. OIDC providers when you need them."),
                    Feature(Ui.IconName.Clock, "Background jobs", "jobs", "Enqueued, delayed and recurring work stored in your database and run by a hosted worker — at-least-once, with exponential backoff."),
                    Feature(Ui.IconName.Envelope, "Transactional email", "mail", "Mail queued on the same database and delivered over SMTP off the request thread; the bodies are components."),
                    Feature(Ui.IconName.Outbox, "Transactional outbox", "outbox", "Domain events committed in the same transaction as the data that raised them, then delivered at-least-once — crash-safe, no message broker."),
                    Feature(Ui.IconName.Bolt, "Cache", "cache", "A database-backed ", Code["IDistributedCache"], " plus ", Code["Cache.Remember(key, load).For(10.Minutes)"], " — or point it at the Redis you already run."),
                    Feature(Ui.IconName.Storage, "File storage", "file-storage", Code["Rask.Storage"], " keeps uploads on disk, in S3-compatible storage or in Azure Blob, with a row per file — public or expiring links, and the content type sniffed from the bytes."),
                    Feature(Ui.IconName.Signal, "Realtime subscriptions", "subscriptions", Code["QueryClient.Subscribe<OrderPlaced>()"], " in a page, and every ", Code["OrderPlaced"], " published afterwards re-renders it — narrowed by a record, opened only through a policy."),
                    Feature(Ui.IconName.Bell, "Web Push", "webpush", "Send Web Push from your backend on your own VAPID keys (RFC 8292/8291) — zero external dependencies."),
                    Feature(Ui.IconName.Stack, "Multi-tenancy", "multi-tenancy", Code["Tenancy.PerTenant"], " on a table adds the tenant column, the query filter and tenant-prefixed indexes; ", Code["Tenant.Across()"], " is the one greppable way around them."),
                    Feature(Ui.IconName.Search, "Full-text search", "full-text-search", Code["Product.Read.Search(\"red anvil\")"], " — ranked, best match first, with highlights and snippets, on SQLite FTS5 or PostgreSQL."),
                    Feature(Ui.IconName.Overview, "The operator console", "dashboard", "A dashboard at ", Code["/_rask"], " over every battery's own table — queue depth, dead letters and the errors behind them, cache, a log tail, SQLite status. Fail-closed behind an authorization policy."),
                    Feature(Ui.IconName.Terminal, "One CLI", "cli", Code["rask new"], " scaffolds a working app with data, auth and the batteries wired; ", Code["rask dev"], " runs it with hot reload; ", Code["rask db"], " migrates and backs up."),
                    Feature(Ui.IconName.Rocket, "One-command deploy", "deployment", Code["rask deploy"], " takes a bare VPS to a live HTTPS site — Docker, a non-root deploy user, firewall + SSH hardening, and zero-downtime swaps.")
                ]
            ]
        ];

    // ---- frontend ----
    //
    // The page's own proof leads it: a component's whole source beside the component, running. It sat in
    // the hero while the page was about UI; the hero is the stack now, and the counter is still the
    // shortest honest answer to "what is a Rask component".
    private Component FrontendSection() =>
        Section.Id(FrontendSectionId).Class(SectionPad)[
            Div.Class(Wrap)[
                SecHead("Frontend · C# components",
                    "The UI is C# too.",
                    "Components are plain C# classes — no ", Code[".razor"], ", no JavaScript, no second language. Roslyn source generators build each one's chain and typed route URLs, trim-safe and reflection-free."),
                // The same 34rem track the hero uses, for the same reason: the counter's longest line is
                // 510px, and a narrower window would scroll it.
                Div.Class("mb-10 grid items-start gap-4 lg:grid-cols-[minmax(0,34rem)_minmax(0,1fr)]")[
                    CodeWindow(),
                    Div.Class("min-w-0")[LiveCounter]
                ],
                Div.Class("grid gap-4 sm:grid-cols-2 lg:grid-cols-3")[
                    Feature(Ui.IconName.Cube, "Components as a chain", "building-components", "A chain surface per component — ", Code["Card.Title(…)"], " — that demands what the component can't do without, plus type-safe ", Code["Routes.*"], " URL builders. Rename a route, break the build — never a dead link."),
                    Feature(Ui.IconName.Clipboard, "Forms & validation", "forms", Code["Form<T>"], " with two-way binding, plus inline, DataAnnotations, FluentValidation, and async validators — the same rules checked again on the server."),
                    Feature(Ui.IconName.PaintBrush, "Scoped CSS & TypeScript", "js-interop", "Drop a sibling ", Code["{Component}.css"], "/", Code[".ts"], ". Auto-scoped, no leaks — a mismatch is a build error. Tailwind v4 compiles from ", Code["dotnet build"], ", with no npm and no config file."),
                    Feature(Ui.IconName.Desktop, "A typed UI kit", "ui-kit", "Every daisyUI component as a C# component — ", Code["Ui.Button"], ", ", Code["Ui.DataGrid"], ", ", Code["Ui.Tree"], " — accessible and themed, with no npm and no Tailwind config."),
                    Feature(Ui.IconName.Phone, $"{BrowserApiCount} typed browser APIs", "browser-apis", "Storage, clipboard, geolocation, passkeys, share, sensors, observers, WebRTC, serial/USB/HID/Bluetooth — one awaitable C# layer, identical on Server & WASM."),
                    Feature(Ui.IconName.Download, "Installable PWA", "pwa", "A typed manifest, a default service worker, offline and background sync — the ", Code["wasm"], " template is installable out of the box."),
                    Feature(Ui.IconName.Retry, "C# Hot Reload", "getting-started", "Edit ", Code["Render()"], " or scoped css/js under ", Code["rask dev"], " and it re-renders live — the closest a compiled framework gets to a no-build loop."),
                    Feature(Ui.IconName.Sparkles, "Prerendering", "prerendering", "A WASM app renders every route to real HTML at publish, so a crawler is served the page rather than a spinner. On the server, every page is live, and its first response waits for its data."),
                    Feature(Ui.IconName.ShieldOk, "70+ compile-time diagnostics", "diagnostics", "A missing required step, a public setter on an aggregate, an image with no alt text — each is a RASK error at build time that names the fix, several with an IDE quick-fix.")
                ]
            ]
        ];

    /// <summary>How many typed browser-API wrappers ship, as the Frontend section counts them.</summary>
    /// <remarks>
    /// Every injectable wrapper service: the 40 every host registers
    /// (<c>RaskHostContracts.BrowserApis</c>) plus the 13 only the WASM host can run
    /// (<c>RaskWasmBrowserApis</c>). The capability matrix lists 51 of them — <c>IViewTransitions</c> and
    /// <c>IWebAnimations</c> have no page of their own under docs/apis/ and are documented in the reference
    /// guide instead. <c>BrowserApiCountTests</c> recounts the source and fails when this goes stale.
    /// </remarks>
    internal const int BrowserApiCount = 53;

    // ---- install ----
    private Component InstallSection() =>
        Section.Class(SectionPad)[
            Div.Class(Wrap)[
                Div.Class("mx-auto mb-10 max-w-2xl text-center")[
                    P.Class($"{Eyebrow} justify-center")["Prerequisite · .NET 10 or 11 SDK"],
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
                        NavLink.Href(PageMeta.LinkTo(Rask.Site.Features.Routes.GuidesIndexPage())).ActiveClass("").Class(BtnPrimary)["Docs"],
                        A
                            .Class(BtnGhost)
                            .Href("https://github.com/pal-tamas/rask")
                            .Target("_blank")
                            .Rel("noopener")[
                            Ui.Icon.Name(Ui.IconName.Star).Class("size-4 shrink-0"), "Star on GitHub"
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
                        NavLink.Href(PageMeta.LinkTo(Rask.Site.Features.Routes.GuidesIndexPage())).ActiveClass("")["Docs"],
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
