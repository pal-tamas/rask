namespace Rask.Example.Auth.Jwt;

// Minimal shell. JwtBootstrap (headless) re-establishes the principal from ProtectedSessionStorage on a
// fresh session/refresh; Router renders the matched page.
public sealed class App : Component
{
    protected override RenderResult Head => Title()["Rask — JWT auth sample"];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body()[
                    JwtBootstrap(),
                    Router()
                ]
            ]
        ];
}
