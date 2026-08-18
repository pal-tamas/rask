namespace Rask.Core.Components;

/// <summary>
///     A tappable button inside a <see cref="AppBar" />. On the web hosts it renders a real
///     <c>&lt;button&gt;</c>; inside a native shell it is projected to a <c>UIBarButtonItem</c> (iOS) or a
///     toolbar action (Android) and emits no HTML at all.
/// </summary>
/// <remarks>
///     <see cref="OnClick" /> is an ordinary Rask callback: it runs on the render thread and re-renders the
///     component that owns the bar, on every host. Nothing about the handler is host-specific — only where the
///     tap comes from.
/// </remarks>
public sealed partial class BarButton : Component
{
    /// <summary>The button's icon. Required — a bar button is an icon affordance.</summary>
    public required BarIcon Icon { get; set; }

    /// <summary>
    ///     The button's accessible name, and its visible text on the web. Strongly recommended: an icon
    ///     button with no name is unusable with a screen reader. For an icon-only look, hide the text
    ///     visually in CSS rather than dropping it — the name has to survive.
    ///     <c>new</c> for the same reason as <see cref="AppBar.Title" />.
    /// </summary>
    public new string? Title { get; set; }

    /// <summary>Invoked when the button is tapped. Optional — a button may be display-only.</summary>
    public Action? OnClick { get; set; }

    protected override Component? Render() =>
        // Inside a native shell the bar is a real platform widget, built from these properties by the native
        // host. Contributing markup here would leave a stray button in the WebView underneath it.
        IsNative
            ? null
            : Button
                .Type("button")
                .Class("rask-bar-button")
                .Data(new Dictionary<string, string?> { ["rask-icon"] = Icon.Name })
                .OnClick(OnClick)[Title ?? string.Empty];
}
