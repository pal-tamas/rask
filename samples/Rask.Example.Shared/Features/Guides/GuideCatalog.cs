using System.Reflection;

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
public sealed record GuideEntry(string Slug, string Title, string Blurb, string Icon, string Group, string? Source = null);

public static class GuideCatalog
{
    public static readonly GuideEntry[] All =
    [
        // ---- Start here ----
        new("one-person-framework", "The One Person Framework",
            "The doctrine: one dev, one codebase, one server, a whole product.", "bi-person-workspace", "Start here"),
        new("getting-started", "Getting started", "Scaffold a project and build your first component.",
            "bi-rocket-takeoff", "Start here"),
        new("best-practices", "Best practices", "Production patterns for state, forms, security, and perf.",
            "bi-stars", "Start here"),
        new("migration-from-blazor", "Migrating from Blazor", "Concept mapping and behavioural differences.",
            "bi-arrow-left-right", "Start here"),
        new("cheatsheet", "Cheat sheet", "Every CLI command, feature flag, and wiring one-liner on one page.",
            "bi-card-list", "Start here"),
        new("recipes", "Recipes", "Task-first: how do I add a feature, gate a page, run a job, deploy?",
            "bi-journal-code", "Start here"),
        new("roadmap", "Roadmap", "The One Person Framework pillars — shipped and planned.",
            "bi-signpost-split", "Start here"),

        // ---- Tutorial ----
        new("00-overview", "Ch 0 · Overview", "What you'll build: a whole product, one pillar per chapter.",
            "bi-flag", "Tutorial", "tutorial/00-overview.md"),
        new("01-scaffold", "Ch 1 · Scaffold", "Scaffold the app with rask new.",
            "bi-1-circle", "Tutorial", "tutorial/01-scaffold.md"),
        new("02-first-feature", "Ch 2 · First feature", "Generate a CRUD feature and wire the database.",
            "bi-2-circle", "Tutorial", "tutorial/02-first-feature.md"),
        new("03-orders-and-auth", "Ch 3 · Orders & auth", "A second feature, and locking it down.",
            "bi-3-circle", "Tutorial", "tutorial/03-orders-and-auth.md"),
        new("04-background-jobs", "Ch 4 · Background jobs", "Run work off the request thread.",
            "bi-4-circle", "Tutorial", "tutorial/04-background-jobs.md"),
        new("05-email", "Ch 5 · Email", "Transactional email off the request thread.",
            "bi-5-circle", "Tutorial", "tutorial/05-email.md"),
        new("06-cache", "Ch 6 · Cache", "Cache the catalog on your own database.",
            "bi-6-circle", "Tutorial", "tutorial/06-cache.md"),
        new("07-outbox-events", "Ch 7 · Outbox & events", "Domain events with the transactional outbox.",
            "bi-7-circle", "Tutorial", "tutorial/07-outbox-events.md"),
        new("08-production-sqlite", "Ch 8 · Production SQLite", "WAL, pragmas, and continuous backup.",
            "bi-8-circle", "Tutorial", "tutorial/08-production-sqlite.md"),
        new("09-web-push", "Ch 9 · Push", "Send Web Push from your own server, on your own keys.",
            "bi-9-circle", "Tutorial", "tutorial/09-web-push.md"),
        new("10-ops", "Ch 10 · Watching it run", "An ops page over every pillar's own table.",
            "bi-activity", "Tutorial", "tutorial/10-ops.md"),
        new("11-deploy", "Ch 11 · Deploy", "Ship to one box with rask deploy.",
            "bi-rocket-takeoff", "Tutorial", "tutorial/11-deploy.md"),

        // ---- One Person Framework (the batteries) ----
        new("cli", "The rask CLI", "Scaffold, run, generate, db, deploy — the front door.", "bi-terminal",
            "One Person Framework"),
        new("data", "Rask.Data", "Base entity + EF Core interceptors: audit, soft-delete, domain events.",
            "bi-database-gear", "One Person Framework"),
        new("cqrs", "CQRS", "Source-generated queries, commands, notifications, behaviors.",
            "bi-shuffle", "One Person Framework"),
        new("jobs", "Background jobs", "Durable enqueued / delayed / recurring work on your database.",
            "bi-clock-history", "One Person Framework"),
        new("mail", "Transactional email", "Durable email queued on your database, delivered over SMTP.",
            "bi-envelope", "One Person Framework"),
        new("cache", "Cache", "A database-backed IDistributedCache plus a typed ICache.",
            "bi-lightning-charge", "One Person Framework"),
        new("outbox", "Outbox", "Crash-safe domain-event delivery on your database.", "bi-outbox",
            "One Person Framework"),
        new("sqlite", "Production SQLite", "WAL + busy-timeout pragmas, continuous backup, snapshots.",
            "bi-database", "One Person Framework"),
        new("sqlite-crdt", "Multi-writer SQLite", "Many replicas of one database, merged per column through plain EF Core.",
            "bi-diagram-3", "One Person Framework"),
        new("sqlite-crdt-sync", "Sharing a CRDT database", "Several devices, one database, a bucket — and no server between them.",
            "bi-cloud-arrow-up", "One Person Framework"),
        new("databases", "Choosing a database", "SQLite by default, PostgreSQL when one box isn't enough.",
            "bi-hdd-stack", "One Person Framework"),
        new("deployment", "Deployment", "rask deploy: a bare VPS to a live HTTPS site, zero downtime.",
            "bi-rocket", "One Person Framework"),
        new("scaling", "Scaling", "How far one box goes, measured — and where the wall actually is.",
            "bi-graph-up", "One Person Framework"),
        new("secrets", "Secrets", "Where passwords and API keys live, and how they reach the server.",
            "bi-key", "One Person Framework"),

        // ---- Core ----
        new("elements", "Elements & the DSL", "Primitives, tag factories, universal props, SVG, the element catalog.",
            "bi-code-square", "Core"),
        new("routing", "Routing", "Route attributes, params, nested layouts, type-safe URLs.",
            "bi-signpost-2", "Core"),
        new("composition", "Composition", "Children, fragments, callbacks, context, virtualize.",
            "bi-diagram-3", "Core"),
        new("composition-callbacks-context", "Composition — callbacks & context", "Child→parent callbacks and provide/consume context.",
            "bi-diagram-3", "Core"),
        new("composition-lists", "Composition — lists & more", "Virtualize, keyed lists, toasts, drag-and-drop, error boundaries.",
            "bi-diagram-3", "Core"),
        new("lifecycle", "Lifecycle", "Mount, props-changed, rendered, unmount, cancellation.",
            "bi-arrow-repeat", "Core"),
        new("forms", "Forms & validation", "Two-way binding, Form<T>, inline/DataAnnotations/Fluent.",
            "bi-input-cursor-text", "Core"),
        new("forms-validation", "Forms — validation", "Inline, DataAnnotations, FluentValidation, and async validators.",
            "bi-input-cursor-text", "Core"),
        new("forms-advanced", "Forms — advanced", "Nested/complex models, radio & checkbox groups, custom controls.",
            "bi-input-cursor-text", "Core"),
        new("js-interop", "JavaScript interop", "Scoped CSS/JS, element refs, IJSRuntime, typed APIs.",
            "bi-braces", "Core"),
        new("js-interop-runtime", "JS interop — runtime", "Calling JS, the typed browser-API layer, element refs, third-party libs.",
            "bi-braces", "Core"),

        // ---- Bootstrap ----
        new("bootstrap", "Bootstrap", "Setup, color modes, the component map, and versioning.",
            "bi-bootstrap", "Bootstrap"),
        new("bootstrap-layout", "Layout", "BsContainer, BsRow/BsCol, BsStack — the page shell and grid.",
            "bi-grid-3x2-gap", "Bootstrap"),
        new("bootstrap-buttons", "Buttons & badges", "BsButton, BsButtonGroup, BsBadge, BsCloseButton.",
            "bi-hand-index", "Bootstrap"),
        new("bootstrap-cards", "Cards, lists & tables",
            "BsCard, BsListGroup, BsPlaceholder, BsTable, BsPagination, BsBreadcrumb.", "bi-card-heading", "Bootstrap"),
        new("data-grid", "Data grid",
            "BsDataGrid — typed columns, sorting, paging, footer totals, master-detail.", "bi-table", "Bootstrap"),
        new("data-grid-server", "Data grid — server-side", "Server paging/sorting, loading state, URL-driven grid state.",
            "bi-table", "Bootstrap"),
        new("data-grid-advanced", "Data grid — advanced",
            "Master-detail, footer totals, custom cell templates, and column features.", "bi-table", "Bootstrap"),
        new("bootstrap-feedback", "Alerts, spinners & progress", "BsAlert, BsSpinner, BsProgress.",
            "bi-exclamation-triangle", "Bootstrap"),
        new("bootstrap-icons", "Icons", "The typed BsIcon over every Bootstrap Icons glyph.",
            "bi-emoji-smile", "Bootstrap"),
        new("bootstrap-navigation", "Navbar & nav", "BsNavbar/BsNav/BsNavItem — SPA-routed, auto-active.",
            "bi-signpost-2", "Bootstrap"),
        new("bootstrap-overlays", "Modals, offcanvas & dropdowns",
            "BsModal/BsOffcanvas/BsDropdown + the fixed-position popover helper.", "bi-window-stack", "Bootstrap"),
        new("bootstrap-disclosure", "Tabs, accordion & collapse", "BsTabs, BsAccordion, BsCollapse — controlled, zero-JS.",
            "bi-list-nested", "Bootstrap"),
        new("bootstrap-toasts", "Toasts", "BsToast + the BsToaster outlet for IToaster messages.",
            "bi-bell", "Bootstrap"),
        new("bootstrap-forms", "Form controls",
            "BsInput/BsTextarea/BsCheck/BsRadioGroup/BsCheckboxGroup + layout helpers.", "bi-input-cursor-text", "Bootstrap"),
        new("bootstrap-select", "Selects & multiselect",
            "The searchable, keyboard-contained BsSelect/BsMultiSelect comboboxes.", "bi-menu-app", "Bootstrap"),
        new("bootstrap-pickers", "Date & time pickers",
            "Hand-editable BsDatePicker/BsTimePicker/BsDateTimePicker.", "bi-calendar-date", "Bootstrap"),
        new("bootstrap-utilities", "Utility classes", "Typed utility tokens composed with Bs.Join(...).",
            "bi-palette", "Bootstrap"),

        // ---- Integration ----
        new("authentication", "Authentication", "Cookie/JWT/OIDC on Server and WASM, route guards.",
            "bi-shield-lock", "Integration"),
        new("authentication-cookie", "Auth — cookie", "Cookie login and session on Server and on a WASM SPA with an API host.",
            "bi-shield-lock", "Integration"),
        new("authentication-jwt", "Auth — JWT", "Bearer-token JWT auth on Server, WASM+host, and standalone static WASM.",
            "bi-shield-lock", "Integration"),
        new("authentication-providers", "Auth — providers", "Identity, Keycloak, Auth0, and other OIDC providers.",
            "bi-people", "Integration"),
        new("authentication-hardening", "Auth — hardening", "Production hardening for cookies, tokens, and sessions.",
            "bi-shield-check", "Integration"),
        new("http-and-files", "HTTP & files", "Fetch JSON with a DI'd HttpClient; upload and download files.",
            "bi-arrow-down-up", "Integration"),
        new("data-access", "Data access", "EF Core + SQLite, vertical slices, DDD patterns.",
            "bi-database", "Integration"),
        new("accessibility", "Accessibility", "ARIA, focus management, the img-alt analyzer.",
            "bi-universal-access", "Integration"),
        new("dashboard", "Dashboard", "An operator dashboard over every battery's table.",
            "bi-speedometer2", "Integration"),
        new("logging", "Logging", "A durable log store in a database of its own.",
            "bi-journal-text", "Integration"),
        new("observability", "Observability", "Logging, tracing, diagnostics.",
            "bi-activity", "Integration"),
        new("configuration", "Configuration", "App configuration and settings.",
            "bi-sliders", "Integration"),

        // ---- Mobile & devices ----
        new("browser-apis", "Browser APIs", "The typed wrappers over the platform's browser APIs.",
            "bi-globe", "Mobile & devices"),
        new("browser-apis-sharing", "Browser APIs — sharing model", "Where wrappers live; declarative vs imperative; subscriptions.",
            "bi-globe", "Mobile & devices"),
        new("browser-apis-reference", "Browser APIs — reference & demos", "Every typed browser wrapper with a runnable live demo.",
            "bi-globe", "Mobile & devices"),
        new("pwa", "Mobile & PWA", "Service workers, Web Push, offline, installable apps.",
            "bi-phone", "Mobile & devices"),
        new("native", "Native (iOS/Android)",
            "WebView-hybrid native host — INativeWebView bridge, safe-area insets, native bars.",
            "bi-phone-fill", "Mobile & devices"),
        new("native-bridge", "Native — modes & bridge", "Local vs Server, INativeWebView, platform heads, asset serving.",
            "bi-phone-fill", "Mobile & devices"),
        new("native-devices", "Native — device capabilities", "Safe-area insets, device backends, native header/footer.",
            "bi-phone-fill", "Mobile & devices"),
        new("sync", "Offline-first merge", "Hybrid logical clock, op log, per-field merge — and why conflicts are reported.",
            "bi-arrow-repeat", "Mobile & devices"),
        new("sync-client", "Syncing between devices", "SyncEngine over a bucket: own-prefix writes, watermarks, offline queue, status.",
            "bi-cloud-arrow-up", "Mobile & devices"),
        new("webpush", "Web Push (server)", "Send Web Push from your backend — VAPID keys, IWebPushSender, delivery results.",
            "bi-send", "Mobile & devices"),
        new("object-storage", "Object storage", "S3 and Azure Blob with no cloud SDK — ranged reads, streaming writes, conditional create.",
            "bi-bucket", "Mobile & devices"),

        // ---- Browser API reference ----
        new("browser-capabilities", "Capability matrix", "Which browser/device API works on which host.",
            "bi-ui-checks-grid", "Browser API reference"),
        new("badge", "IBadge", "Typed browser API: IBadge.", "bi-plug", "Browser API reference", "apis/badge.md"),
        new("battery", "IBattery", "Typed browser API: IBattery.", "bi-plug", "Browser API reference", "apis/battery.md"),
        new("bluetooth", "IBluetooth", "Typed browser API: IBluetooth.", "bi-plug", "Browser API reference", "apis/bluetooth.md"),
        new("broadcast-channel", "IBroadcastChannel", "Typed browser API: IBroadcastChannel.", "bi-plug", "Browser API reference", "apis/broadcast-channel.md"),
        new("clipboard", "IClipboard", "Typed browser API: IClipboard.", "bi-plug", "Browser API reference", "apis/clipboard.md"),
        new("cookies", "ICookies", "Typed browser API: ICookies.", "bi-plug", "Browser API reference", "apis/cookies.md"),
        new("crypto", "ICrypto", "Typed browser API: ICrypto.", "bi-plug", "Browser API reference", "apis/crypto.md"),
        new("device-motion", "IDeviceMotion", "Typed browser API: IDeviceMotion.", "bi-plug", "Browser API reference", "apis/device-motion.md"),
        new("device-orientation", "IDeviceOrientation", "Typed browser API: IDeviceOrientation.", "bi-plug", "Browser API reference", "apis/device-orientation.md"),
        new("eye-dropper", "IEyeDropper", "Typed browser API: IEyeDropper.", "bi-plug", "Browser API reference", "apis/eye-dropper.md"),
        new("file-system-access", "IFileSystemAccess", "Typed browser API: IFileSystemAccess.", "bi-plug", "Browser API reference", "apis/file-system-access.md"),
        new("fullscreen", "IFullscreen", "Typed browser API: IFullscreen.", "bi-plug", "Browser API reference", "apis/fullscreen.md"),
        new("gamepad", "IGamepad", "Typed browser API: IGamepad.", "bi-plug", "Browser API reference", "apis/gamepad.md"),
        new("geolocation", "IGeolocation", "Typed browser API: IGeolocation.", "bi-plug", "Browser API reference", "apis/geolocation.md"),
        new("hid", "IHid", "Typed browser API: IHid.", "bi-plug", "Browser API reference", "apis/hid.md"),
        new("idle-detector", "IIdleDetector", "Typed browser API: IIdleDetector.", "bi-plug", "Browser API reference", "apis/idle-detector.md"),
        new("indexeddb", "IIndexedDb", "Typed browser API: IIndexedDb.", "bi-plug", "Browser API reference", "apis/indexeddb.md"),
        new("install-prompt", "IInstallPrompt", "Typed browser API: IInstallPrompt.", "bi-plug", "Browser API reference", "apis/install-prompt.md"),
        new("intersection-observer", "IIntersectionObserver", "Typed browser API: IIntersectionObserver.", "bi-plug", "Browser API reference", "apis/intersection-observer.md"),
        new("media-devices", "IMediaDevices", "Typed browser API: IMediaDevices.", "bi-plug", "Browser API reference", "apis/media-devices.md"),
        new("media-query", "IMediaQuery", "Typed browser API: IMediaQuery.", "bi-plug", "Browser API reference", "apis/media-query.md"),
        new("media-session", "IMediaSession", "Typed browser API: IMediaSession.", "bi-plug", "Browser API reference", "apis/media-session.md"),
        new("media-streams", "IMediaStreams", "Typed browser API: IMediaStreams.", "bi-plug", "Browser API reference", "apis/media-streams.md"),
        new("mutation-observer", "IMutationObserver", "Typed browser API: IMutationObserver.", "bi-plug", "Browser API reference", "apis/mutation-observer.md"),
        new("navigator-info", "INavigatorInfo", "Typed browser API: INavigatorInfo.", "bi-plug", "Browser API reference", "apis/navigator-info.md"),
        new("network-info", "INetworkInfo", "Typed browser API: INetworkInfo.", "bi-plug", "Browser API reference", "apis/network-info.md"),
        new("notifications", "INotifications", "Typed browser API: INotifications.", "bi-plug", "Browser API reference", "apis/notifications.md"),
        new("origin-private-file-system", "IOriginPrivateFileSystem", "Typed browser API: IOriginPrivateFileSystem.", "bi-plug", "Browser API reference", "apis/origin-private-file-system.md"),
        new("page-visibility", "IPageVisibility", "Typed browser API: IPageVisibility.", "bi-plug", "Browser API reference", "apis/page-visibility.md"),
        new("performance", "IPerformance", "Typed browser API: IPerformance.", "bi-plug", "Browser API reference", "apis/performance.md"),
        new("permissions", "IPermissions", "Typed browser API: IPermissions.", "bi-plug", "Browser API reference", "apis/permissions.md"),
        new("picture-in-picture", "IPictureInPicture", "Typed browser API: IPictureInPicture.", "bi-plug", "Browser API reference", "apis/picture-in-picture.md"),
        new("resize-observer", "IResizeObserver", "Typed browser API: IResizeObserver.", "bi-plug", "Browser API reference", "apis/resize-observer.md"),
        new("screen-info", "IScreenInfo", "Typed browser API: IScreenInfo.", "bi-plug", "Browser API reference", "apis/screen-info.md"),
        new("screen-orientation", "IScreenOrientation", "Typed browser API: IScreenOrientation.", "bi-plug", "Browser API reference", "apis/screen-orientation.md"),
        new("serial", "ISerial", "Typed browser API: ISerial.", "bi-plug", "Browser API reference", "apis/serial.md"),
        new("signaling", "ISignaling", "Typed browser API: ISignaling.", "bi-plug", "Browser API reference", "apis/signaling.md"),
        new("share", "IShare", "Typed browser API: IShare.", "bi-plug", "Browser API reference", "apis/share.md"),
        new("speech-recognition", "ISpeechRecognition", "Typed browser API: ISpeechRecognition.", "bi-plug", "Browser API reference", "apis/speech-recognition.md"),
        new("speech-synthesis", "ISpeechSynthesis", "Typed browser API: ISpeechSynthesis.", "bi-plug", "Browser API reference", "apis/speech-synthesis.md"),
        new("storage-estimator", "IStorageEstimator", "Typed browser API: IStorageEstimator.", "bi-plug", "Browser API reference", "apis/storage-estimator.md"),
        new("storage", "IBrowserStorage", "Typed browser API: IBrowserStorage.", "bi-plug", "Browser API reference", "apis/storage.md"),
        new("usb", "IUsb", "Typed browser API: IUsb.", "bi-plug", "Browser API reference", "apis/usb.md"),
        new("vibration", "IVibration", "Typed browser API: IVibration.", "bi-plug", "Browser API reference", "apis/vibration.md"),
        new("visual-viewport", "IVisualViewport", "Typed browser API: IVisualViewport.", "bi-plug", "Browser API reference", "apis/visual-viewport.md"),
        new("wake-lock", "IWakeLock", "Typed browser API: IWakeLock.", "bi-plug", "Browser API reference", "apis/wake-lock.md"),
        new("web-locks", "IWebLocks", "Typed browser API: IWebLocks.", "bi-plug", "Browser API reference", "apis/web-locks.md"),
        new("web-push", "IWebPush", "Typed browser API: IWebPush.", "bi-plug", "Browser API reference", "apis/web-push.md"),
        new("webauthn", "IWebAuthn", "Typed browser API: IWebAuthn.", "bi-plug", "Browser API reference", "apis/webauthn.md"),
        new("webrtc", "IWebRtc", "Typed browser API: IWebRtc.", "bi-plug", "Browser API reference", "apis/webrtc.md"),

        // ---- Advanced ----
        new("testing", "Testing", "Unit testing with Rask.Testing, event handlers, E2E.",
            "bi-clipboard-check", "Advanced"),
        new("building-form-controls", "Building form controls", "Author your own IFormControl<T>.",
            "bi-tools", "Advanced"),
        new("aot", "AOT compilation", "Ahead-of-time compile for WASM, and trim-safety.",
            "bi-cpu", "Advanced"),
        new("playground", "Live playground", "The in-browser Roslyn playground.",
            "bi-easel", "Advanced"),
        new("code-analysis", "Code analysis", "The analyzers and warnings-as-errors adoption.",
            "bi-search", "Advanced"),
        new("diagnostics", "Diagnostics", "Every RASK0xx descriptor, its trigger, and the fix.",
            "bi-exclamation-diamond", "Advanced"),

        // ---- Contributing & internals ----
        new("development-workflow", "Development workflow", "How the repo builds, tests, and ships.",
            "bi-diagram-2", "Contributing & internals"),
        new("repo-administration", "Repo administration", "Governance, CODEOWNERS, releases, automation.",
            "bi-github", "Contributing & internals"),
        new("ai-agents", "Building with AI assistants", "Conventions for AI coding agents working on Rask.",
            "bi-robot", "Contributing & internals"),
        new("live-rendering", "Live-rendering internals", "The diff codec and the live-render pipeline.",
            "bi-cpu-fill", "Contributing & internals", "architecture/live-rendering.md"),
        new("live-rendering-codec", "Live rendering — walk & codec", "Parallel HTML+frame walk, the edit-op diff codec, keyed reconciliation.",
            "bi-cpu-fill", "Contributing & internals", "architecture/live-rendering-codec.md"),
        new("live-rendering-runtime", "Live rendering — cache & dispatch", "SessionRenderCache, head/query-nav, handler ordering, slow-connection.",
            "bi-cpu-fill", "Contributing & internals", "architecture/live-rendering-runtime.md")
    ];

    public static readonly string[] GroupOrder =
        ["Start here", "Tutorial", "One Person Framework", "Core", "Bootstrap", "Integration",
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
