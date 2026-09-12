using Rask.Core;

namespace Rask.DevTools.Fixture.Wasm;

public sealed partial class App : Component
{
    private int _clicks;

    protected override Component? HeadAssets => [
        Title["Rask.DevTools WASM fixture"],
        Meta.Charset("utf-8"),
    ];

    // A handler, so a Debug run has page traffic for the devtools panel to list; the publish gate is indifferent to it.
    protected override Component? Render() =>
        Main[
            H1["Rask.DevTools WASM fixture"],
            P[$"clicks={_clicks}"],
            Button.Id("fixture-click").OnClick(() => _clicks++)["click"]
        ];
}
