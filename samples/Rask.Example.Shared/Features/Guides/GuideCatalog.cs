using System.Reflection;
using Rask.Ui;

namespace Rask.Example.Shared.Features;

// The complete set of repo guides surfaced on-site, in display order and grouped. Each entry's Slug is a
// docs file's bare leaf name (docs/routing.md and docs/apis/geolocation.md -> "routing"/"geolocation"),
// which is exactly what Markdown.RewriteLinks routes an in-doc "*.md" link to (/guides/{leaf}) and what
// the /guides/{slug} route binds. Leaf names are unique across docs/ (guarded by the build). For a guide
// that lives in a subfolder, Source carries the real docs-relative path for the "edit on GitHub" link;
// top-level guides leave it null (defaulting to "{Slug}.md").
//
// Shared by GuidesIndexPage (the cards) and the sidebar's Guides section so the two never drift.
// GuidesTests guards both directions: every slug resolves to an embedded doc, and every embedded
// user-facing doc appears here — so a doc can never be added to the repo yet hidden from the site.
public sealed record GuideEntry(string Slug, string Title, string Blurb, string Group, string? Source = null)
{
    /// <summary>The group's icon.</summary>
    /// <remarks>
    /// Derived rather than stored. Every guide used to name its own, which meant 67 distinct glyphs
    /// across ~80 guides — and the sidebar already groups them, so the icon only ever repeated what the
    /// heading said. One per group is the information that was actually there, and a guide added later
    /// cannot forget to pick one.
    /// </remarks>
    public UiIconName Icon => Group switch
    {
        "Start here" => UiIconName.Rocket,
        "Tutorial" => UiIconName.Book,
        "One Person Framework" => UiIconName.Bolt,
        "Core" => UiIconName.Cube,
        "Integration" => UiIconName.ArrowsRightLeft,
        "Advanced" => UiIconName.Sparkles,
        "Mobile & devices" => UiIconName.Phone,
        "Contributing & internals" => UiIconName.Terminal,
        _ => UiIconName.Document,
    };
}

public static class GuideCatalog
{
    public static readonly GuideEntry[] All =
    [
        // ---- Start here ----
        new("one-person-framework", "The One Person Framework",
            "The doctrine: one dev, one codebase, one server, a whole product.", "Start here"),
        new("installation", "Installing Rask", "One line to the CLI and everything it needs — options, upgrade, uninstall.", "Start here"),
        new("getting-started", "Getting started", "Scaffold a project and build your first component.", "Start here"),
        new("best-practices", "Best practices", "Production patterns for state, forms, security, and perf.", "Start here"),
        new("migration-from-blazor", "Migrating from Blazor", "Concept mapping and behavioural differences.", "Start here"),
        new("cheatsheet", "Cheat sheet", "Every CLI command, feature flag, and wiring one-liner on one page.", "Start here"),
        new("recipes", "Recipes", "Task-first: how do I add a feature, gate a page, run a job, deploy?", "Start here"),
        new("roadmap", "Roadmap", "The One Person Framework pillars — shipped and planned.", "Start here"),

        // ---- Tutorial ----
        new("00-overview", "Ch 0 · Overview", "What you'll build: a whole product, one pillar per chapter.", "Tutorial", "tutorial/00-overview.md"),
        new("01-scaffold", "Ch 1 · Scaffold", "Scaffold the app with rask new.", "Tutorial", "tutorial/01-scaffold.md"),
        new("02-first-feature", "Ch 2 · First feature", "Generate a CRUD feature and wire the database.", "Tutorial", "tutorial/02-first-feature.md"),
        new("03-orders-and-auth", "Ch 3 · Orders & auth", "A second feature, and locking it down.", "Tutorial", "tutorial/03-orders-and-auth.md"),
        new("04-background-jobs", "Ch 4 · Background jobs", "Run work off the request thread.", "Tutorial", "tutorial/04-background-jobs.md"),
        new("05-email", "Ch 5 · Email", "Transactional email off the request thread.", "Tutorial", "tutorial/05-email.md"),
        new("06-cache", "Ch 6 · Cache", "Cache the catalog on your own database.", "Tutorial", "tutorial/06-cache.md"),
        new("07-outbox-events", "Ch 7 · Outbox & events", "Domain events with the transactional outbox.", "Tutorial", "tutorial/07-outbox-events.md"),
        new("08-production-sqlite", "Ch 8 · Production SQLite", "WAL, pragmas, and continuous backup.", "Tutorial", "tutorial/08-production-sqlite.md"),
        new("09-web-push", "Ch 9 · Push", "Send Web Push from your own server, on your own keys.", "Tutorial", "tutorial/09-web-push.md"),
        new("10-ops", "Ch 10 · Watching it run", "An ops page over every pillar's own table.", "Tutorial", "tutorial/10-ops.md"),
        new("11-deploy", "Ch 11 · Deploy", "Ship to one box with rask deploy.", "Tutorial", "tutorial/11-deploy.md"),

        // ---- One Person Framework (the batteries) ----
        new("cli", "The rask CLI", "Scaffold, run, generate, db, deploy — the front door.",
            "One Person Framework"),
        new("data", "Rask.Data", "Base entity + EF Core interceptors: audit, soft-delete, domain events.", "One Person Framework"),
        new("cqrs", "CQRS", "Source-generated queries, commands, notifications, behaviors.", "One Person Framework"),
        new("spa", "TypeScript front ends", "React, Vue, Angular and four more, typed from your C# contracts.", "One Person Framework"),
        new("meta", "Meta framework front ends",
            "Nuxt, Next, SvelteKit and three more owning the whole front end — one container.", "One Person Framework"),
        new("islands", "Islands",
            "A .tsx or Lit file as an ordinary Rask component, with props owned by C#.", "One Person Framework"),
        new("blazor-components", "Blazor components",
            "A real Blazor component — MudBlazor, an RCL — hosted in a Rask page, server-rendered.", "One Person Framework"),
        new("tailwind", "Tailwind CSS", "Tailwind v4 compiled by dotnet build — no npm, no config file.", "One Person Framework"),
        new("query", "Rask.Query", "The dispatcher wrapped in a cache: dedup, staleness, invalidation.", "One Person Framework"),
        new("jobs", "Background jobs", "Durable enqueued / delayed / recurring work on your database.", "One Person Framework"),
        new("mail", "Transactional email", "Durable email queued on your database, delivered over SMTP.", "One Person Framework"),
        new("cache", "Cache", "A database-backed IDistributedCache plus a typed ICache.", "One Person Framework"),
        new("outbox", "Outbox", "Crash-safe domain-event delivery on your database.",
            "One Person Framework"),
        new("sqlite", "Production SQLite", "WAL + busy-timeout pragmas, continuous backup, snapshots.", "One Person Framework"),
        new("deployment", "Deployment", "rask deploy: a bare VPS to a live HTTPS site, zero downtime.", "One Person Framework"),
        new("scaling", "Scaling", "How far one box goes, measured — and where the wall actually is.", "One Person Framework"),
        new("secrets", "Secrets", "Where passwords and API keys live, and how they reach the server.", "One Person Framework"),

        // ---- Core ----
        new("building-components", "Building components",
            "Naming a component and chaining onto it; what a component demands before it exists.", "Core"),
        new("elements", "Elements & the DSL", "Primitives, tag factories, universal props, SVG, the element catalog.", "Core"),
        new("routing", "Routing", "Route attributes, params, nested layouts, type-safe URLs.", "Core"),
        new("composition", "Composition", "Children, fragments, callbacks, context, virtualize.", "Core"),
        new("composition-callbacks-context", "Composition — callbacks & context", "Child→parent callbacks and provide/consume context.", "Core"),
        new("composition-lists", "Composition — lists & more", "Virtualize, keyed lists, toasts, drag-and-drop, error boundaries.", "Core"),
        new("lifecycle", "Lifecycle", "Mount, props-changed, rendered, unmount, cancellation.", "Core"),
        new("render-modes", "Render modes", "Waiting for async data before the first byte, static pages, status codes.", "Core"),
        new("forms", "Forms & validation", "Two-way binding, Form<T>, inline/DataAnnotations/Fluent.", "Core"),
        new("forms-validation", "Forms — validation", "Inline, DataAnnotations, FluentValidation, and async validators.", "Core"),
        new("forms-advanced", "Forms — advanced", "Nested/complex models, radio & checkbox groups, custom controls.", "Core"),
        new("js-interop", "JavaScript interop", "Scoped CSS/TypeScript, element refs, IJSRuntime, typed APIs.", "Core"),
        new("js-interop-runtime", "JS interop — runtime", "Calling JS, the typed browser-API layer, element refs, third-party libs.", "Core"),


        // ---- Integration ----
        new("authentication", "Authentication", "Cookie/JWT/OIDC on Server and WASM, route guards.", "Integration"),
        new("authentication-cookie", "Auth — cookie", "Cookie login and session on Server and on a WASM SPA with an API host.", "Integration"),
        new("authentication-jwt", "Auth — JWT", "Bearer-token JWT auth on Server, WASM+host, and standalone static WASM.", "Integration"),
        new("authentication-providers", "Auth — providers", "Identity, Keycloak, Auth0, and other OIDC providers.", "Integration"),
        new("authentication-hardening", "Auth — hardening", "Production hardening for cookies, tokens, and sessions.", "Integration"),
        new("http-and-files", "HTTP & files", "Fetch JSON with a DI'd HttpClient; upload and download files.", "Integration"),
        new("data-access", "Data access", "EF Core + SQLite, vertical slices, DDD patterns.", "Integration"),
        new("accessibility", "Accessibility", "ARIA, focus management, the img-alt analyzer.", "Integration"),
        new("localization", "Localization", "Ship in more than one language: negotiated culture, typed catalogs, plurals.", "Integration"),
        new("dashboard", "Dashboard", "An operator dashboard over every battery's table.", "Integration"),
        new("ui-kit", "UI kit", "The components the framework's own surfaces are drawn with.", "Integration"),
        new("logging", "Logging", "A durable log store in a database of its own.", "Integration"),
        new("observability", "Observability", "Logging, tracing, diagnostics.", "Integration"),
        new("configuration", "Configuration", "App configuration and settings.", "Integration"),

        // ---- Mobile & devices ----
        new("browser-apis", "Browser APIs", "The typed wrappers over the platform's browser APIs.", "Mobile & devices"),
        new("browser-apis-sharing", "Browser APIs — sharing model", "Where wrappers live; declarative vs imperative; subscriptions.", "Mobile & devices"),
        new("browser-apis-reference", "Browser APIs — reference & demos", "Every typed browser wrapper with a runnable live demo.", "Mobile & devices"),
        new("pwa", "Mobile & PWA", "Service workers, Web Push, offline, installable apps.", "Mobile & devices"),
        new("webpush", "Web Push (server)", "Send Web Push from your backend — VAPID keys, IWebPush, delivery results.", "Mobile & devices"),

        // ---- Browser API reference ----
        new("browser-capabilities", "Capability matrix", "Which browser/device API works on which host.", "Browser API reference"),
        new("background-sync", "IBackgroundSync", "Typed browser API: IBackgroundSync.", "Browser API reference", "apis/background-sync.md"),
        new("badge", "IBadge", "Typed browser API: IBadge.", "Browser API reference", "apis/badge.md"),
        new("battery", "IBattery", "Typed browser API: IBattery.", "Browser API reference", "apis/battery.md"),
        new("bluetooth", "IBluetooth", "Typed browser API: IBluetooth.", "Browser API reference", "apis/bluetooth.md"),
        new("broadcast-channel", "IBroadcastChannel", "Typed browser API: IBroadcastChannel.", "Browser API reference", "apis/broadcast-channel.md"),
        new("clipboard", "IClipboard", "Typed browser API: IClipboard.", "Browser API reference", "apis/clipboard.md"),
        new("cookies", "ICookies", "Typed browser API: ICookies.", "Browser API reference", "apis/cookies.md"),
        new("crypto", "ICrypto", "Typed browser API: ICrypto.", "Browser API reference", "apis/crypto.md"),
        new("device-motion", "IDeviceMotion", "Typed browser API: IDeviceMotion.", "Browser API reference", "apis/device-motion.md"),
        new("device-orientation", "IDeviceOrientation", "Typed browser API: IDeviceOrientation.", "Browser API reference", "apis/device-orientation.md"),
        new("eye-dropper", "IEyeDropper", "Typed browser API: IEyeDropper.", "Browser API reference", "apis/eye-dropper.md"),
        new("file-system-access", "IFileSystemAccess", "Typed browser API: IFileSystemAccess.", "Browser API reference", "apis/file-system-access.md"),
        new("fullscreen", "IFullscreen", "Typed browser API: IFullscreen.", "Browser API reference", "apis/fullscreen.md"),
        new("gamepad", "IGamepad", "Typed browser API: IGamepad.", "Browser API reference", "apis/gamepad.md"),
        new("geolocation", "IGeolocation", "Typed browser API: IGeolocation.", "Browser API reference", "apis/geolocation.md"),
        new("hid", "IHid", "Typed browser API: IHid.", "Browser API reference", "apis/hid.md"),
        new("idle-detector", "IIdleDetector", "Typed browser API: IIdleDetector.", "Browser API reference", "apis/idle-detector.md"),
        new("indexeddb", "IIndexedDb", "Typed browser API: IIndexedDb.", "Browser API reference", "apis/indexeddb.md"),
        new("install-prompt", "IInstallPrompt", "Typed browser API: IInstallPrompt.", "Browser API reference", "apis/install-prompt.md"),
        new("intersection-observer", "IIntersectionObserver", "Typed browser API: IIntersectionObserver.", "Browser API reference", "apis/intersection-observer.md"),
        new("media-devices", "IMediaDevices", "Typed browser API: IMediaDevices.", "Browser API reference", "apis/media-devices.md"),
        new("media-query", "IMediaQuery", "Typed browser API: IMediaQuery.", "Browser API reference", "apis/media-query.md"),
        new("media-session", "IMediaSession", "Typed browser API: IMediaSession.", "Browser API reference", "apis/media-session.md"),
        new("media-streams", "IMediaStreams", "Typed browser API: IMediaStreams.", "Browser API reference", "apis/media-streams.md"),
        new("mutation-observer", "IMutationObserver", "Typed browser API: IMutationObserver.", "Browser API reference", "apis/mutation-observer.md"),
        new("navigator-info", "INavigatorInfo", "Typed browser API: INavigatorInfo.", "Browser API reference", "apis/navigator-info.md"),
        new("network-info", "INetworkInfo", "Typed browser API: INetworkInfo.", "Browser API reference", "apis/network-info.md"),
        new("notifications", "INotifications", "Typed browser API: INotifications.", "Browser API reference", "apis/notifications.md"),
        new("origin-private-file-system", "IOriginPrivateFileSystem", "Typed browser API: IOriginPrivateFileSystem.", "Browser API reference", "apis/origin-private-file-system.md"),
        new("page-visibility", "IPageVisibility", "Typed browser API: IPageVisibility.", "Browser API reference", "apis/page-visibility.md"),
        new("performance", "IPerformance", "Typed browser API: IPerformance.", "Browser API reference", "apis/performance.md"),
        new("permissions", "IPermissions", "Typed browser API: IPermissions.", "Browser API reference", "apis/permissions.md"),
        new("picture-in-picture", "IPictureInPicture", "Typed browser API: IPictureInPicture.", "Browser API reference", "apis/picture-in-picture.md"),
        new("resize-observer", "IResizeObserver", "Typed browser API: IResizeObserver.", "Browser API reference", "apis/resize-observer.md"),
        new("screen-info", "IScreenInfo", "Typed browser API: IScreenInfo.", "Browser API reference", "apis/screen-info.md"),
        new("screen-orientation", "IScreenOrientation", "Typed browser API: IScreenOrientation.", "Browser API reference", "apis/screen-orientation.md"),
        new("serial", "ISerial", "Typed browser API: ISerial.", "Browser API reference", "apis/serial.md"),
        new("signaling", "ISignaling", "Typed browser API: ISignaling.", "Browser API reference", "apis/signaling.md"),
        new("share", "IShare", "Typed browser API: IShare.", "Browser API reference", "apis/share.md"),
        new("speech-recognition", "ISpeechRecognition", "Typed browser API: ISpeechRecognition.", "Browser API reference", "apis/speech-recognition.md"),
        new("speech-synthesis", "ISpeechSynthesis", "Typed browser API: ISpeechSynthesis.", "Browser API reference", "apis/speech-synthesis.md"),
        new("storage-estimator", "IStorageEstimator", "Typed browser API: IStorageEstimator.", "Browser API reference", "apis/storage-estimator.md"),
        new("storage", "IBrowserStorage", "Typed browser API: IBrowserStorage.", "Browser API reference", "apis/storage.md"),
        new("usb", "IUsb", "Typed browser API: IUsb.", "Browser API reference", "apis/usb.md"),
        new("vibration", "IVibration", "Typed browser API: IVibration.", "Browser API reference", "apis/vibration.md"),
        new("visual-viewport", "IVisualViewport", "Typed browser API: IVisualViewport.", "Browser API reference", "apis/visual-viewport.md"),
        new("wake-lock", "IWakeLock", "Typed browser API: IWakeLock.", "Browser API reference", "apis/wake-lock.md"),
        new("web-locks", "IWebLocks", "Typed browser API: IWebLocks.", "Browser API reference", "apis/web-locks.md"),
        new("web-push", "IWebPush", "Typed browser API: IWebPush.", "Browser API reference", "apis/web-push.md"),
        new("webauthn", "IWebAuthn", "Typed browser API: IWebAuthn.", "Browser API reference", "apis/webauthn.md"),
        new("webrtc", "IWebRtc", "Typed browser API: IWebRtc.", "Browser API reference", "apis/webrtc.md"),

        // ---- Advanced ----
        new("testing", "Testing", "Unit testing with Rask.Testing, event handlers, E2E.", "Advanced"),
        new("building-form-controls", "Building form controls", "Author your own IFormControl<T>.", "Advanced"),
        new("aot", "AOT compilation", "Ahead-of-time compile for WASM, and trim-safety.", "Advanced"),
        new("prerendering", "Prerendering", "Render a standalone WASM app's pages to HTML at publish.", "Advanced"),
        new("playground", "Live playground", "The in-browser Roslyn playground.", "Advanced"),
        new("code-analysis", "Code analysis", "The analyzers and warnings-as-errors adoption.", "Advanced"),
        new("api-style", "Public API style", "How every public name is chosen, and the gate that records the surface.", "Advanced"),
        new("diagnostics", "Diagnostics", "Every RASK0xx descriptor, its trigger, and the fix.", "Advanced"),

        // ---- Contributing & internals ----
        new("development-workflow", "Development workflow", "How the repo builds, tests, and ships.", "Contributing & internals"),
        new("repo-administration", "Repo administration", "Governance, CODEOWNERS, releases, automation.", "Contributing & internals"),
        new("ai-agents", "Building with AI assistants", "Conventions for AI coding agents working on Rask.", "Contributing & internals"),
        new("live-rendering", "Live-rendering internals", "The diff codec and the live-render pipeline.", "Contributing & internals", "architecture/live-rendering.md"),
        new("live-rendering-codec", "Live rendering — walk & codec", "Parallel HTML+frame walk, the edit-op diff codec, keyed reconciliation.", "Contributing & internals", "architecture/live-rendering-codec.md"),
        new("live-rendering-runtime", "Live rendering — cache & dispatch", "SessionRenderCache, head/query-nav, handler ordering, slow-connection.", "Contributing & internals", "architecture/live-rendering-runtime.md")
    ];

    public static readonly string[] GroupOrder =
        ["Start here", "Tutorial", "One Person Framework", "Core", "Integration",
         "Mobile & devices", "Browser API reference", "Advanced", "Contributing & internals"];

    public static string TitleFor(string slug)
    {
        foreach (var g in All)
        {
            if (g.Slug == slug)
            {
                return g.Title;
            }
        }

        return slug;
    }

    // The doc's path under docs/, for the "edit on GitHub" link. Subfolder guides carry it explicitly
    // (the slug is only the bare leaf); top-level guides default to "{slug}.md".
    public static string SourcePath(string slug)
    {
        foreach (var g in All)
        {
            if (g.Slug == slug)
            {
                return g.Source ?? $"{slug}.md";
            }
        }

        return $"{slug}.md";
    }

    // Reads the verbatim markdown for a guide. Every docs/**/*.md is embedded as raskdoc/{leaf}.md (see
    // the EmbeddedResource glob in Rask.Example.Shared.csproj), so the slug — the bare leaf — is the key.
    // Returns null for an unknown slug, so GuidePage can render a not-found state instead of a blank page.
    public static string? ReadMarkdown(string slug)
    {
        var asm = typeof(GuideCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream($"raskdoc/{slug}.md");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
