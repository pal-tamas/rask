namespace Company.RaskServer.Features.Shared;

// The document around this — charset, the UI kit's stylesheet and theme, your Styles/app.css — is Rask's. A page
// says what it is called with a PageTitle override, and the line below puts that in the tab.
public sealed partial class App(RouteState route) : Component
{
    protected override Component? HeadAssets => [
        Title[route.Title is { } page ? $"{page} | Company.RaskServer" : "Company.RaskServer"],
        // Edge to edge on a phone, as an app is: the kit's header, sidebar, flyouts and toasts keep clear of the notch.
        Meta.Name("viewport").Content("width=device-width, initial-scale=1, viewport-fit=cover"),
        // The browser's own bar in the page's ground, light and dark.
        Meta.Name("theme-color").Content("#ffffff").Media("(prefers-color-scheme: light)"),
        Meta.Name("theme-color").Content("#27272a").Media("(prefers-color-scheme: dark)")
    ];

    protected override Component? Render() => Router;
}
