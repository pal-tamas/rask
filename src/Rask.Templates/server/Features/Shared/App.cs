namespace Company.RaskServer.Features.Shared;

// The document around this — charset, viewport, the UI kit's stylesheet and theme, your Styles/app.css —
// is Rask's. A page says what it is called with a PageTitle override, and the line below puts that in the tab.
public sealed partial class App(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } page ? $"{page} | Company.RaskServer" : "Company.RaskServer"];

    protected override Component? Render() => Router;
}
