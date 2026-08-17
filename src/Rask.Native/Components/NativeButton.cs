using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A tappable button, projected to a <c>UIButton</c> (iOS) or a <c>MaterialButton</c> (Android).
/// </summary>
/// <remarks>Its label is its <b>children</b>, the same spelling as <c>Button["Save"]</c> on the web.</remarks>
/// <example>
///     <code>NativeButton.Style(NativeButtonStyle.Filled).OnClickAsync(SaveAsync)["Save"]</code>
/// </example>
public sealed partial class NativeButton : NativeViewComponent
{
    /// <summary>The visual treatment. Leave <c>null</c> for <see cref="NativeButtonStyle.Filled" />.</summary>
    /// <remarks>
    ///     This carried <c>new</c> until the HTML element family moved into <c>Rask.Html</c>: it shadowed
    ///     the generated <c>Style</c> markup entry, which a native component has no use for. Those entries
    ///     are no longer injected into native components, so there is nothing left to shadow and <c>new</c>
    ///     became CS0109 — which is the outcome the split was for. A tag is not in scope where only
    ///     platform views can render.
    /// </remarks>
    public NativeButtonStyle? Style { get; set; }

    /// <summary>The title color. Leave <c>null</c> to let <see cref="Style" /> decide.</summary>
    public NativeColor? Color { get; set; }

    /// <summary>The background color. Leave <c>null</c> to let <see cref="Style" /> decide.</summary>
    public NativeColor? Background { get; set; }

    /// <summary>Whether the button accepts taps. Leave <c>null</c> for enabled.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Invoked when the button is tapped.</summary>
    public Action? OnClick { get; set; }

    /// <summary>
    ///     The awaited form of <see cref="OnClick" /> — use it for a handler that saves, fetches or otherwise
    ///     awaits. The framework awaits it before re-rendering, so state set after the <c>await</c> still
    ///     paints. Setting both runs the synchronous one first.
    /// </summary>
    public Func<Task>? OnClickAsync { get; set; }

    /// <summary>An accessibility identifier for screen readers and on-device E2E.</summary>
    public string? AccessibilityId { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Button;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Text(NativePropId.Text, ChildText());
        props.Enum(NativePropId.Style, Style);
        props.Color(NativePropId.Color, Color);
        props.Color(NativePropId.Background, Background);
        props.Flag(NativePropId.Enabled, Enabled);
        props.Handler(NativePropId.TapId, OnClick ?? (Delegate?)OnClickAsync, SurfaceTapId);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
