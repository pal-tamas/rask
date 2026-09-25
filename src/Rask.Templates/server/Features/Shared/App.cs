namespace Company.RaskServer.Features.Shared;

// The document around this — charset, viewport, the UI kit's stylesheet and theme, your Styles/app.css —
// is Rask's. A page adds its own <head> tags with a HeadAssets override of its own.
public sealed partial class App : Component
{
    protected override Component? HeadAssets => Title["Company.RaskServer"];

    protected override Component? Render() => Router;
}
