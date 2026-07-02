using Rask.Core.Live;

namespace Rask.Bootstrap;

// Links the bundled Bootstrap (and, by default, Bootstrap Icons) stylesheets. The CSS ships as static
// web assets under _content/Rask.Bootstrap and is served by the host's MapStaticAssets() on Server and
// by the static-web-assets pipeline on WASM. Drop BootstrapStyles() in your App's Head. URLs are
// prefixed with LiveOptions.PathBase so sub-path deploys resolve.
public sealed class BootstrapStyles : Component
{
    private const string Base = "/_content/Rask.Bootstrap/";

    // Include the Bootstrap Icons stylesheet (default true). Set false if you don't use BsIcon.
    public bool? Icons { get; set; }

    protected override Component? Render()
    {
        var prefix = LiveOptions.PathBase;
        var core = Link(Rel: "stylesheet", Href: prefix + Base + "css/bootstrap.min.css");

        return Icons is false
            ? core
            : [core, Link(Rel: "stylesheet", Href: prefix + Base + "icons/bootstrap-icons.min.css")];
    }
}
