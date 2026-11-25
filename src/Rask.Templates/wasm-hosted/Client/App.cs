namespace Company.RaskServer.Client;

// The document around this — charset, viewport, the UI kit's stylesheet and theme — is Rask's. A page says
// what it is called with a PageTitle override, and the Title below puts that in the tab.
public sealed partial class App(RouteState route) : Component
{
    protected override Component? HeadAssets => [
        Title[route.Title is { } page ? $"{page} | Company.RaskServer" : "Company.RaskServer"],
        // Compiled from the server's Styles/app.css (its build scans this folder too) and served beside the
        // browser app, so this project cannot announce it itself.
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
    ];

    // The page, and the dialog every page asks its questions in: an editor's Delete, Ui.Confirm.Ask from a handler.
    // (Toasts need nothing here: the host draws them when the app places no Ui.Toast of its own.)
    protected override Component? Render() => [Router, Ui.Confirm];
}
