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
        // Built here, rendered inside the frame: on the page the child sits under DevToolsTestFrame, not beside it.
        DevToolsTestFrame[DevToolsTestChild.Caption("hello")]
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

/// <summary>
///     A page that goes wrong on request: its first button's handler throws, and its second makes the next render of a
///     child throw.
/// </summary>
public sealed partial class DevToolsErrorsTestApp : Component
{
    private bool _broken;

    protected override Component? Render() =>
    [
        Button.OnClick(Throw)["throw"],
        Button.OnClick(() => _broken = true)["break"],
        DevToolsTestFrame[_broken ? DevToolsThrowingChild : DevToolsTestChild.Caption("fine")]
    ];

    // A method group, not a throwing lambda: `() => throw …` fits both the sync and the async handler shape.
    private static void Throw() => throw new InvalidOperationException("the handler failed on purpose");
}

/// <summary>A component whose render always throws.</summary>
public sealed partial class DevToolsThrowingChild : Component
{
    /// <inheritdoc />
    protected override Component? Render() => throw new InvalidOperationException("the render failed on purpose");
}

/// <summary>Renders what it is given inside a section, so the tree has a child that another component built.</summary>
public sealed partial class DevToolsTestFrame : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Section[Children ?? []];
}
