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
        Button.OnClick(() => _clicks++)["click"],
        DevToolsTestChild.Caption("hello")
    ];
}

/// <summary>
///     A child with a property, so the tree the panel takes has a component on it that was GIVEN something. The
///     app itself keeps its state in a field, which is not a prop and is not described.
/// </summary>
public sealed partial class DevToolsTestChild : Component
{
    /// <summary>Rendered on the page, and described to the panel by the override the build writes.</summary>
    public string? Caption { get; set; }

    /// <inheritdoc />
    protected override Component? Render() => Span[Caption ?? string.Empty];
}
