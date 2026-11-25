namespace Company.RaskServer.Features.Shared;

// The document around this — charset, viewport, the UI kit's stylesheet and theme, your Styles/app.css —
// is Rask's. A page says what it is called with a PageTitle override, and the line below puts that in the tab.
public sealed partial class App(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } page ? $"{page} | Company.RaskServer" : "Company.RaskServer"];

    // The page, and the dialog every page asks its questions in: an editor's Delete, Ui.Confirm.Ask from a handler.
    // (Toasts need nothing here: the host draws them when the app places no Ui.Toast of its own.)
    protected override Component? Render() => [Router, Ui.Confirm];
}
