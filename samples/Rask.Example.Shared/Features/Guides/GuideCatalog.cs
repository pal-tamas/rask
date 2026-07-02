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
        new("browser-apis", "Browser APIs", "The typed wrappers over the platform's browser APIs.",
            "bi-globe", "Core"),

        new("bootstrap", "Bootstrap", "The typed Rask.Bootstrap component library.",
            "bi-bootstrap", "Integration"),
        new("authentication", "Authentication", "Cookie/JWT/OIDC on Server and WASM, route guards.",
            "bi-shield-lock", "Integration"),
        new("data-access", "Data access", "EF Core + SQLite, vertical slices, DDD patterns.",
            "bi-database", "Integration"),
        new("accessibility", "Accessibility", "ARIA, focus management, the img-alt analyzer.",
            "bi-universal-access", "Integration"),
        new("pwa", "Mobile & PWA", "Service workers, Web Push, offline, installable apps.",
            "bi-phone", "Integration"),
        new("observability", "Observability", "Logging, tracing, diagnostics.",
            "bi-activity", "Integration"),
        new("configuration", "Configuration", "App configuration and settings.",
            "bi-sliders", "Integration"),

        new("testing", "Testing", "Unit testing with Rask.TestSupport, event handlers, E2E.",
            "bi-clipboard-check", "Advanced"),
        new("building-form-controls", "Building form controls", "Author your own IFormControl<T>.",
            "bi-tools", "Advanced"),
        new("code-analysis", "Code analysis", "The analyzers and warnings-as-errors adoption.",
            "bi-search", "Advanced"),
        new("diagnostics", "Diagnostics", "Every RASK0xx descriptor, its trigger, and the fix.",
            "bi-exclamation-diamond", "Advanced")
    ];

    public static readonly string[] GroupOrder = ["Start here", "Core", "Integration", "Advanced"];

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
