namespace Rask.Example.Auth.WasmJwt;

public sealed class App : Component
{
    protected override RenderResult Head => Title()["Rask — JWT + WASM auth"];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body()[Router()]
            ]
        ];
}
