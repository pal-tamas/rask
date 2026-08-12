using Rask.Core.Routing;

namespace Rask.Example.Shop.Features.Shared;

public sealed partial class App : Component
{
    // App-level head contributions splice into the framework-managed <head>
    // via the Component? HeadAssets override. Title is singleton — any page that
    // overrides HeadAssets with its own Title supersedes this fallback for the tab.
    protected override Component? HeadAssets => [
        Title["Rask.Example.Shop"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        // Bootstrap 5.3 + Icons via Rask.Bootstrap (served from _content/Rask.Bootstrap).
        BootstrapStyles
    ];

    // The body's content. Rask emits the doctype, <html lang>, <head> and <body> around this —
    // override HtmlLang / BodyClass for their attributes, or Shell(head, body) for the rest.
    protected override Component? Render() => Router;
}
