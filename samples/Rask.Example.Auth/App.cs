namespace Rask.Example.Auth;

// Minimal app shell: the framework-managed <head> plus a Router that renders the matched page.
public sealed class App : Component
{
    protected override RenderResult Head => Title()["Rask — cookie auth sample"];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body()[Router()]
            ]
        ];
}
