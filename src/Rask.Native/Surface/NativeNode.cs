namespace Rask.Native.Surface;

/// <summary>
///     The kind of platform view a <see cref="NativeNode" /> describes. A closed enum rather than a string so a
///     surface backend switches over it exhaustively — the compiler tells an iOS/Android head when a new kind
///     lands and it has nothing to materialize for it.
/// </summary>
public enum NativeNodeKind
{
    /// <summary>The content root of a pure-native page — see <c>NativeScreen</c>.</summary>
    Screen,

    /// <summary>A linear box, vertical or horizontal — see <c>NativeStack</c>.</summary>
    Stack,

    /// <summary>A scrolling viewport around a single child — see <c>NativeScroll</c>.</summary>
    Scroll,

    /// <summary>A keyed, vertically scrolling list of rows — see <c>NativeList</c>.</summary>
    List,

    /// <summary>Read-only text — see <c>NativeLabel</c>.</summary>
    Label,

    /// <summary>A tappable button — see <c>NativeButton</c>.</summary>
    Button,

    /// <summary>A single-line text input — see <c>NativeTextField</c>.</summary>
    TextField,

    /// <summary>An on/off toggle — see <c>NativeSwitch</c>.</summary>
    Switch,

    /// <summary>An image — see <c>NativeImage</c>.</summary>
    Image,

    /// <summary>A spinning progress indicator — see <c>NativeActivityIndicator</c>.</summary>
    ActivityIndicator,

    /// <summary>A hairline separator — see <c>NativeDivider</c>.</summary>
    Divider,

    /// <summary>Flexible empty space that pushes its siblings apart — see <c>NativeSpacer</c>.</summary>
    Spacer,
}

/// <summary>
///     Identifies a single property on a <see cref="NativeNode" />. Props are carried as a flat
///     <c>(id, value)</c> list rather than per-kind classes so the differ compares two nodes by walking one
///     sorted span each — no per-kind casting, and a backend reads only the ids its widget understands.
/// </summary>
public enum NativePropId
{
    /// <summary>Label/button text, or the text field's current value.</summary>
    Text,

    /// <summary>A text field's placeholder.</summary>
    Placeholder,

    /// <summary>Stack orientation: <c>0</c> vertical, <c>1</c> horizontal.</summary>
    Orientation,

    /// <summary>Gap between a stack's children, in device-independent points.</summary>
    Spacing,

    /// <summary>Uniform inner padding, in device-independent points.</summary>
    Padding,

    /// <summary>Cross-axis alignment: see <c>NativeAlignment</c>.</summary>
    Alignment,

    /// <summary>Font size in points.</summary>
    FontSize,

    /// <summary>Font weight: see <c>NativeFontWeight</c>.</summary>
    FontWeight,

    /// <summary>Text alignment: see <c>NativeTextAlign</c>.</summary>
    TextAlign,

    /// <summary>Maximum line count; <c>0</c> means unlimited.</summary>
    Lines,

    /// <summary>Foreground/text color, as a <c>NativeColor</c> token.</summary>
    Color,

    /// <summary>Background color, as a <c>NativeColor</c> token.</summary>
    Background,

    /// <summary>Button style: see <c>NativeButtonStyle</c>.</summary>
    Style,

    /// <summary>An image source — a bundled asset name or a URL.</summary>
    Source,

    /// <summary>Image scaling mode: see <c>NativeContentMode</c>.</summary>
    ContentMode,

    /// <summary>Whether a text field masks its input.</summary>
    Secure,

    /// <summary>Keyboard type for a text field: see <c>NativeKeyboardType</c>.</summary>
    Keyboard,

    /// <summary>Whether a switch is on.</summary>
    On,

    /// <summary>Whether the control accepts interaction.</summary>
    Enabled,

    /// <summary>Whether an activity indicator is spinning.</summary>
    Animating,

    /// <summary>Fixed width in points; absent means "size to content".</summary>
    Width,

    /// <summary>Fixed height in points; absent means "size to content".</summary>
    Height,

    /// <summary>
    ///     The tap handler's id, present only when the component supplied one — its absence is what tells a
    ///     backend not to attach a target/gesture recognizer at all. The delegate itself never crosses the
    ///     boundary: the backend echoes this id back in a <c>NativeSurfaceEvent</c> and the session resolves it
    ///     against the handler map it rebuilds every render, so a tap always reaches the latest closure.
    ///     <para>
    ///         Ids are owned by the component instance, not by its position, so they survive structural churn
    ///         elsewhere in the tree — otherwise removing one row would renumber every interactive node below
    ///         it and force a prop patch on each.
    ///     </para>
    /// </summary>
    TapId,

    /// <summary>The value-change handler's id (a text field or a switch); absent when the component supplied none.</summary>
    ChangeId,

    /// <summary>An accessibility label / identifier, used by screen readers and the on-device E2E.</summary>
    AccessibilityId,
}

/// <summary>The payload type a <see cref="NativePropValue" /> carries.</summary>
public enum NativePropKind
{
    /// <summary>
    ///     No value — the prop was present last frame and is gone now. Only ever appears in a
    ///     <c>SetProps</c> patch, where it means "reset this property to the platform default"; a node's own
    ///     prop list never carries one, because an unset prop is simply absent from it.
    /// </summary>
    None,

    /// <summary>A string — text, a color token, an image source.</summary>
    Text,

    /// <summary>A number — a size, a spacing, or an enum member's integer value.</summary>
    Number,

    /// <summary>A boolean flag.</summary>
    Flag,
}

/// <summary>
///     One property value. A three-way struct union rather than <c>object</c> so a frame's props carry no
///     boxing — a screen re-rendering on every keystroke would otherwise allocate a box per numeric prop
///     per node.
/// </summary>
public readonly struct NativePropValue : IEquatable<NativePropValue>
{
    private readonly string? _text;
    private readonly double _number;

    private NativePropValue(NativePropKind kind, string? text, double number)
    {
        Kind = kind;
        _text = text;
        _number = number;
    }

    /// <summary>Which of the three payloads this value carries.</summary>
    public NativePropKind Kind { get; }

    /// <summary>The string payload; <c>null</c> unless <see cref="Kind" /> is <see cref="NativePropKind.Text" />.</summary>
    public string? Text => _text;

    /// <summary>The numeric payload; meaningless unless <see cref="Kind" /> is <see cref="NativePropKind.Number" />.</summary>
    public double Number => _number;

    /// <summary>The boolean payload; meaningless unless <see cref="Kind" /> is <see cref="NativePropKind.Flag" />.</summary>
    public bool Flag => _number != 0;

    /// <summary>Wraps a string (or a color token / image source).</summary>
    public static NativePropValue FromText(string? value) => new(NativePropKind.Text, value, 0);

    /// <summary>Wraps a number — a size, a spacing, or an enum member's integer value.</summary>
    public static NativePropValue FromNumber(double value) => new(NativePropKind.Number, null, value);

    /// <summary>Wraps a boolean flag.</summary>
    public static NativePropValue FromFlag(bool value) => new(NativePropKind.Flag, null, value ? 1 : 0);

    /// <summary>
    ///     The "this prop went away" marker a <c>SetProps</c> patch carries so the backend resets the property
    ///     to its platform default. Without it, clearing a label's color would leave the last color applied.
    /// </summary>
    public static NativePropValue Unset => new(NativePropKind.None, null, 0);

    /// <inheritdoc />
    public bool Equals(NativePropValue other) =>
        Kind == other.Kind
        && _number.Equals(other._number)
        && string.Equals(_text, other._text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NativePropValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, _text, _number);

    /// <summary>Value equality — two props are the same when kind and payload match.</summary>
    public static bool operator ==(NativePropValue left, NativePropValue right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(NativePropValue left, NativePropValue right) => !left.Equals(right);
}

/// <summary>One <c>(id, value)</c> property pair on a node.</summary>
/// <param name="Id">Which property this is.</param>
/// <param name="Value">Its value.</param>
public readonly record struct NativeProp(NativePropId Id, NativePropValue Value);

/// <summary>
///     One node in the native view tree — the platform-agnostic description a surface backend materializes
///     into a real <c>UIView</c> (iOS) or <c>android.view.View</c>. Built fresh each render by
///     <c>NativeTreeBuilder</c> from the render walk, then diffed against the previous frame's tree so the
///     backend receives a minimal patch list instead of a teardown.
/// </summary>
/// <remarks>
///     Props arrive sorted by <see cref="NativeProp.Id" />, which is what lets <c>NativeTreeDiffer</c> compare
///     two nodes with a single merge walk rather than a nested scan or a dictionary per node.
/// </remarks>
public sealed class NativeNode
{
    /// <summary>The platform view this node describes.</summary>
    public required NativeNodeKind Kind { get; init; }

    /// <summary>
    ///     The reconciliation identity taken from the component's <c>Key</c>, or <c>null</c> when it has none.
    ///     Keyed siblings are matched by key across a frame, so reordering a list moves views instead of
    ///     rewriting every row's contents.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>This node's properties, sorted by id.</summary>
    public NativeProp[] Props { get; init; } = [];

    /// <summary>This node's children, in order.</summary>
    public NativeNode[] Children { get; init; } = [];

    /// <summary>Looks up a prop by id, returning whether it was present.</summary>
    public bool TryGetProp(NativePropId id, out NativePropValue value)
    {
        // Props are sorted by id and a node carries a handful at most, so a linear scan that stops early
        // beats a binary search's branchiness at this size.
        foreach (var prop in Props)
        {
            if (prop.Id == id)
            {
                value = prop.Value;
                return true;
            }

            if (prop.Id > id)
            {
                break;
            }
        }

        value = default;
        return false;
    }
}
