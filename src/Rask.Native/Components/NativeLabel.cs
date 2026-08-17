using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     Read-only text, projected to a <c>UILabel</c> (iOS) or a <c>TextView</c> (Android). The pure-native
///     equivalent of a <c>Span</c> — and, like it, the component a screen has the most of, so its props are the
///     ones a diff touches most often.
/// </summary>
/// <remarks>
///     Its content is its <b>children</b>, the same spelling as <c>Span["hi"]</c> — there is no <c>Text</c>
///     property to remember.
/// </remarks>
/// <example>
///     <code>NativeLabel.FontSize(15).Color(NativeColor.Secondary)[$"Signed in as {user.Name}"]</code>
/// </example>
public sealed partial class NativeLabel : NativeViewComponent
{
    /// <summary>Font size in points. Leave <c>null</c> for the platform's body size.</summary>
    public double? FontSize { get; set; }

    /// <summary>Font weight. Leave <c>null</c> for <see cref="NativeFontWeight.Regular" />.</summary>
    public NativeFontWeight? FontWeight { get; set; }

    /// <summary>The text color. Leave <c>null</c> for the platform's primary label color.</summary>
    public NativeColor? Color { get; set; }

    /// <summary>Text alignment. Leave <c>null</c> for <see cref="NativeTextAlign.Start" />.</summary>
    public NativeTextAlign? TextAlign { get; set; }

    /// <summary>Maximum lines before truncating. Leave <c>null</c> or pass <c>0</c> for unlimited.</summary>
    public int? Lines { get; set; }

    /// <summary>An accessibility identifier for screen readers and on-device E2E.</summary>
    public string? AccessibilityId { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.Label;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Text(NativePropId.Text, ChildText());
        props.Number(NativePropId.FontSize, FontSize);
        props.Enum(NativePropId.FontWeight, FontWeight);
        props.Color(NativePropId.Color, Color);
        props.Enum(NativePropId.TextAlign, TextAlign);
        props.Number(NativePropId.Lines, Lines);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
