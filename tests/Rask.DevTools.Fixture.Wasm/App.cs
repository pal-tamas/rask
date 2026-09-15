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
            Button.Id("fixture-click").OnClick(() => _clicks++)["click"],
            // And one that throws, so a Debug run has an error for the panel's Errors tab.
            Button.Id("fixture-throw").OnClick(Throw)["throw"]
        ];

    private static void Throw() => throw new InvalidOperationException("the fixture's handler failed on purpose");
}
