using Rask.Core;

namespace Rask.DevTools.Fixture.Server;

public sealed partial class App : Component
{
    protected override Component? HeadAssets => [
        Title["Rask.DevTools fixture"],
        Meta.Charset("utf-8"),
    ];

    protected override Component? Render() => Main[H1["Rask.DevTools fixture"]];
}
