using Rask.Core.Live;

namespace Rask.Bootstrap;

// Links the bundled Bootstrap (and, by default, Bootstrap Icons) stylesheets plus rask-bootstrap.css —
// a small supplemental sheet with fixes for the zero-JS components (linked after Bootstrap so it wins).
// The CSS ships as static web assets under _content/Rask.Bootstrap and is served by the host's
// MapStaticAssets() on Server and by the static-web-assets pipeline on WASM. Drop BootstrapStyles() in
// your App's Head. URLs are prefixed with LiveOptions.PathBase so sub-path deploys resolve.
public sealed partial class BootstrapStyles : Component
{
    private new const string Base = "/_content/Rask.Bootstrap/";

    // Include the Bootstrap Icons stylesheet (default true). Set false if you don't use BsIcon.
    public bool? Icons { get; set; }

    protected override Component? Render()
    {
        var prefix = LiveOptions.PathBase;
        var core = Link(Rel: "stylesheet", Href: prefix + Base + "css/bootstrap.min.css");
        // Rask's own fixes for the Popper-less components; must come after Bootstrap to win the cascade.
        var fixes = Link(Rel: "stylesheet", Href: prefix + Base + "css/rask-bootstrap.css");

        return Icons is false
            ? [core, fixes]
            : [core, Link(Rel: "stylesheet", Href: prefix + Base + "icons/bootstrap-icons.min.css"), fixes];
    }
}

// Links the shared Rask design tokens (_content/Rask.Bootstrap/tokens.css) — the violet, dark-first
// palette plus the Bootstrap 5.3 --bs-* bridge that reskins every Bs* component to it. Link this AFTER
// BootstrapStyles() (so the --bs-* bridge wins the cascade) and BEFORE the app's own global.css (so app
// CSS can still override the tokens). URLs are PathBase-prefixed so sub-path deploys resolve.
public sealed partial class RaskTokens : Component
{
    protected override Component? Render() =>
        Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/_content/Rask.Bootstrap/tokens.css");
}
