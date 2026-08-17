using Rask.Native.Surface;

namespace Rask.Native.Components;

/// <summary>
///     A single-line text input, projected to a <c>UITextField</c> (iOS) or an <c>EditText</c> (Android).
///     Controlled: what it shows is <see cref="Value" />, and every keystroke raises
///     <see cref="OnInput" />/<see cref="OnInputAsync" /> for you to update the state it reads back.
/// </summary>
/// <example>
///     <code>NativeTextField(Value: query, Placeholder: "Search", OnInput: v => { query = v; })</code>
/// </example>
public sealed partial class NativeTextField : NativeViewComponent
{
    /// <summary>The current text. Leave <c>null</c> for empty.</summary>
    public string? Value { get; set; }

    /// <summary>Placeholder text shown while empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Whether the field masks its input (a password). Leave <c>null</c> for plain text.</summary>
    public bool? Secure { get; set; }

    /// <summary>Which keyboard to raise. Leave <c>null</c> for <see cref="NativeKeyboardType.Default" />.</summary>
    public NativeKeyboardType? Keyboard { get; set; }

    /// <summary>Whether the field accepts input. Leave <c>null</c> for enabled.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Invoked with the new text on every change.</summary>
    public Action<string>? OnInput { get; set; }

    /// <summary>
    ///     The awaited form of <see cref="OnInput" /> — use it for a handler that queries or validates
    ///     asynchronously. Setting both runs the synchronous one first.
    /// </summary>
    public Func<string, Task>? OnInputAsync { get; set; }

    /// <summary>An accessibility identifier for screen readers and on-device E2E.</summary>
    public string? AccessibilityId { get; set; }

    /// <inheritdoc />
    internal override NativeNodeKind SurfaceKind => NativeNodeKind.TextField;

    /// <inheritdoc />
    internal override void WriteSurfaceProps(ref NativePropWriter props)
    {
        props.Text(NativePropId.Text, Value);
        props.Text(NativePropId.Placeholder, Placeholder);
        props.Flag(NativePropId.Secure, Secure);
        props.Enum(NativePropId.Keyboard, Keyboard);
        props.Flag(NativePropId.Enabled, Enabled);
        props.Handler(NativePropId.ChangeId, OnInput ?? (Delegate?)OnInputAsync, SurfaceChangeId);
        props.Text(NativePropId.AccessibilityId, AccessibilityId);
    }
}
