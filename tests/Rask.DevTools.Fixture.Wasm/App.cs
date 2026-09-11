using Rask.Core;

namespace Rask.DevTools.Fixture.Wasm;

public sealed partial class App : Component
{
    protected override Component? HeadAssets => [
        Title["Rask.DevTools WASM fixture"],
        Meta.Charset("utf-8"),
    ];

    protected override Component? Render() => Main[H1["Rask.DevTools WASM fixture"]];
}
