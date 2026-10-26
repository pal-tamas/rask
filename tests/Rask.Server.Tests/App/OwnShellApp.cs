using Rask.Core;

namespace Rask.Server.Tests.App;

// An app that writes its whole document, as an adopted app does: its own head list and its own <html>/<body>.
public sealed partial class OwnShellApp : Component
{
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Ui.SidebarScript,
        Link.Rel("stylesheet").Href("/css/app.css"),
        Title["own-shell"]
    ];

    protected override Component Shell(Component head, Component body) =>
        Html.Lang("hu")[head, Body.Class("min-h-screen bg-white dark:bg-zinc-800")[body]];

    protected override Component? Render() => H1["ok"];
}
