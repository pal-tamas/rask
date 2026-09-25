namespace Company.RaskServer.Client;

// The document around this — charset, viewport, the UI kit's stylesheet and theme — is Rask's. A page adds
// its own <head> tags with a HeadAssets override of its own.
public sealed partial class App : Component
{
    protected override Component? HeadAssets => [
        Title["Company.RaskServer"],
        // Compiled from the server's Styles/app.css (its build scans this folder too) and served beside the
        // browser app, so this project cannot announce it itself.
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/css/app.css")
    ];

    protected override Component? Render() => Router;
}
