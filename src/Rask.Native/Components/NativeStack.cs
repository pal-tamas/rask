using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A linear box — the workhorse layout of a pure-native screen, projected to a <c>UIStackView</c> (iOS) or
///     a <c>LinearLayout</c> (Android). Nest stacks to build rows inside columns, exactly as you would nest
///     flex containers on the web.
/// </summary>
/// <remarks>
///     A stack is tappable: give it <see cref="OnClick" /> (or <see cref="OnClickAsync" />) and the whole box
///     responds, which is how a list row becomes a row you can select without a separate component for it.
/// </remarks>
/// <example>
///     <code>NativeStack(Spacing: 12, Padding: 16)[
///         NativeLabel(Text: "Total", FontWeight: NativeFontWeight.Semibold),
///         NativeLabel(Text: total.ToString("C"))]</code>
/// </example>
public sealed partial class NativeStack : NativeViewComponent
{
    /// <summary>Which way children are laid out. Leave <c>null</c> for <see cref="NativeOrientation.Vertical" />.</summary>
    public NativeOrientation? Orientation { get; set; }

    /// <summary>The gap between children, in points. Leave <c>null</c> for none.</summary>
    public double? Spacing { get; set; }

    /// <summary>Uniform inner padding in points. Leave <c>null</c> for none.</summary>
    public double? Padding { get; set; }

    /// <summary>Cross-axis alignment. Leave <c>null</c> for <see cref="NativeAlignment.Stretch" />.</summary>
    public NativeAlignment? Alignment { get; set; }

    /// <summary>The stack's background color. Leave <c>null</c> for transparent.</summary>
    public NativeColor? Background { get; set; }

    /// <summary>Invoked when the whole box is tapped. Leave <c>null</c> to make it non-interactive.</summary>
    public Action? OnClick { get; set; }

    /// <summary>
    ///     The awaited form of <see cref="OnClick" />. The framework awaits it before re-rendering, so state a
    ///     handler sets after an <c>await</c> still paints. Setting both runs the synchronous one first.
    /// </summary>
    public Func<Task>? OnClickAsync { get; set; }

    /// <summary>An accessibility identifier for screen readers and on-device E2E.</summary>
    public string? AccessibilityId { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Stack;

    /// <inheritdoc />
    internal override bool AcceptsChildren => true;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Enum(NativePropId.Orientation, Orientation);
        props.Number(NativePropId.Spacing, Spacing);
        props.Number(NativePropId.Padding, Padding);
        props.Enum(NativePropId.Alignment, Alignment);
        props.Color(NativePropId.Background, Background);
        props.Handler(NativePropId.TapId, OnClick ?? (Delegate?)OnClickAsync, SurfaceTapId);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
