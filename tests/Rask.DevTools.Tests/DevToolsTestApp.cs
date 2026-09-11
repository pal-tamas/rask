using Rask.Core;

namespace Rask.DevTools.Tests;

/// <summary>
///     The app an endpoint test hosts. It has a handler so its page keeps a live session: a page without one
///     is served static, with no runtime for the devtools to extend and so no devtools stamp.
/// </summary>
public sealed partial class DevToolsTestApp : Component
{
    private int _clicks;

    protected override Component? Render() =>
    [
        P[$"clicks={_clicks}"],
        Button.OnClick(() => _clicks++)["click"]
    ];
}
