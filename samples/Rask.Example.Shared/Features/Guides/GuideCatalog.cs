using System.Reflection;

namespace Rask.Example.Shared.Features;

// The curated set of repo guides surfaced on-site, in display order and grouped. Each entry's Slug is a
// docs/{slug}.md file embedded into this assembly (see the EmbeddedResource glob in the csproj). Shared
// by GuidesIndexPage (the cards) and the sidebar's Guides section so the two never drift.
public sealed record GuideEntry(string Slug, string Title, string Blurb, string Icon, string Group);

public static class GuideCatalog
{
    public static readonly GuideEntry[] All =
    [
        new("getting-started", "Getting started", "Scaffold a project and build your first component.",
            "bi-rocket-takeoff", "Start here"),
        new("best-practices", "Best practices", "Production patterns for state, forms, security, and perf.",
            "bi-stars", "Start here"),
        new("migration-from-blazor", "Migrating from Blazor", "Concept mapping and behavioural differences.",
            "bi-arrow-left-right", "Start here"),

        new("elements", "Elements & the DSL", "Primitives, tag factories, universal props, SVG, the element catalog.",
            "bi-code-square", "Core"),
        new("routing", "Routing", "Route attributes, params, nested layouts, type-safe URLs.",
            "bi-signpost-2", "Core"),
        new("composition", "Composition", "Children, fragments, callbacks, context, virtualize.",
            "bi-diagram-3", "Core"),
        new("lifecycle", "Lifecycle", "Mount, props-changed, rendered, unmount, cancellation.",
            "bi-arrow-repeat", "Core"),
        new("forms", "Forms & validation", "Two-way binding, Form<T>, inline/DataAnnotations/Fluent.",
            "bi-input-cursor-text", "Core"),
        new("js-interop", "JavaScript interop", "Scoped CSS/JS, element refs, IJSRuntime, typed APIs.",
            "bi-braces", "Core"),

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

        // Mobile & devices: the browser-API surface, PWA/installable/offline, and the native iOS/Android
        // host — three overlapping guides kept together as one group (browser-apis.md ↔ pwa.md cross-link).
        new("browser-apis", "Browser APIs", "The typed wrappers over the platform's browser APIs.",
            "bi-globe", "Mobile & devices"),
        new("pwa", "Mobile & PWA", "Service workers, Web Push, offline, installable apps.",
            "bi-phone", "Mobile & devices"),
        new("native", "Native (iOS/Android)",
            "WebView-hybrid native host — INativeWebView bridge, safe-area insets, native bars.",
            "bi-phone-fill", "Mobile & devices"),

        new("authentication", "Authentication", "Cookie/JWT/OIDC on Server and WASM, route guards.",
            "bi-shield-lock", "Integration"),
        new("http-and-files", "HTTP & files", "Fetch JSON with a DI'd HttpClient; upload and download files.",
            "bi-arrow-down-up", "Integration"),
        new("data-access", "Data access", "EF Core + SQLite, vertical slices, DDD patterns.",
            "bi-database", "Integration"),
        new("cqrs", "CQRS", "Source-generated queries, commands, notifications, behaviors.",
            "bi-shuffle", "Integration"),
        new("accessibility", "Accessibility", "ARIA, focus management, the img-alt analyzer.",
            "bi-universal-access", "Integration"),
        new("observability", "Observability", "Logging, tracing, diagnostics.",
            "bi-activity", "Integration"),
        new("configuration", "Configuration", "App configuration and settings.",
            "bi-sliders", "Integration"),

        new("testing", "Testing", "Unit testing with Rask.Testing, event handlers, E2E.",
            "bi-clipboard-check", "Advanced"),
        new("building-form-controls", "Building form controls", "Author your own IFormControl<T>.",
            "bi-tools", "Advanced"),
        new("code-analysis", "Code analysis", "The analyzers and warnings-as-errors adoption.",
            "bi-search", "Advanced"),
        new("diagnostics", "Diagnostics", "Every RASK0xx descriptor, its trigger, and the fix.",
            "bi-exclamation-diamond", "Advanced")
    ];

    public static readonly string[] GroupOrder =
        ["Start here", "Core", "Bootstrap", "Mobile & devices", "Integration", "Advanced"];

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

    // Reads the verbatim markdown for a guide. The docs/{slug}.md files are embedded as raskdoc/{slug}.md
    // (see the EmbeddedResource glob in Rask.Example.Shared.csproj). Returns null for an unknown slug, so
    // GuidePage can render a not-found state instead of a blank page.
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
