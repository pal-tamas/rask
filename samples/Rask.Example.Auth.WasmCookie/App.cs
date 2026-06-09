namespace Rask.Example.Auth.WasmCookie;

public sealed class App : Component
{
    protected override RenderResult Head => Title()["Rask — Cookie + WASM auth"];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body()[Router()]
            ]
        ];
}
