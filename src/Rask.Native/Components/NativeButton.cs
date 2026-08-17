using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A tappable button, projected to a <c>UIButton</c> (iOS) or a <c>MaterialButton</c> (Android).
/// </summary>
/// <example>
///     <code>NativeButton(Text: "Save", Style: NativeButtonStyle.Filled, OnClickAsync: SaveAsync)</code>
/// </example>
public sealed partial class NativeButton : NativeViewComponent
{
    /// <summary>The button's label. Required.</summary>
    /// <remarks>
    ///     Shadows the generated <c>Text</c> markup entry, which a native component has no use for — it renders
    ///     platform views, never HTML.
    /// </remarks>
    public new required string Text { get; set; }

    /// <summary>The visual treatment. Leave <c>null</c> for <see cref="NativeButtonStyle.Filled" />.</summary>
    /// <remarks>Shadows the generated <c>Style</c> markup entry, for the same reason as <see cref="Text" />.</remarks>
    public new NativeButtonStyle? Style { get; set; }

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
        props.Text(NativePropId.Text, Text);
        props.Enum(NativePropId.Style, Style);
        props.Color(NativePropId.Color, Color);
        props.Color(NativePropId.Background, Background);
        props.Flag(NativePropId.Enabled, Enabled);
        props.Handler(NativePropId.TapId, OnClick ?? (Delegate?)OnClickAsync, SurfaceTapId);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
