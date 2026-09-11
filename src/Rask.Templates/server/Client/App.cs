using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Ui;

namespace Company.RaskServer.Client;

public sealed partial class App : Component
{
    // App-level head contributions splice into the document's <head> via the Component? HeadAssets
    // override. Title is singleton — any page that overrides HeadAssets with its own Title supersedes this
    // fallback for the tab.
    protected override Component? HeadAssets => [
        Title["Company.RaskServer"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        // The kit's sheet, FIRST — it declares the @layer order for the whole document.
        // Href() carries a content hash, so it caches hard and still changes when the kit does.
        Link.Rel("stylesheet").Href(UiStylesheet.Href(LiveOptions.PathBase)),
        // Compiled from Styles/app.css by the server's build, which scans this folder too, and served by
        // the server beside the browser app.
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
    ];

    // The body's content. Rask renders the document around this — override HtmlLang / BodyClass for their
    // attributes, or Shell(head, body) for the rest.
    protected override Component? Render() => Router;

    // Turns the UI kit's theme on for the whole document.
    //
    // Load-bearing, and its absence is silent: daisyUI paints :root by default and the kit confines its
    // palette to this attribute, so that referencing the package cannot repaint an app that only wanted a
    // button. Without it every Ui* component renders structurally correct and completely grey.
    protected override Component Shell(Component head, Component body) =>
        Html.Lang(HtmlLang).Dir(HtmlDir).Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
            head,
            Body.Class(BodyClass)[body]
        ];
}
