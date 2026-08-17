namespace Rask.Native.Components;

/// <summary>Which way a <see cref="NativeStack" /> lays its children out.</summary>
public enum NativeOrientation
{
    /// <summary>Top to bottom — a <c>UIStackView</c> with <c>Axis.Vertical</c> / a vertical <c>LinearLayout</c>.</summary>
    Vertical,

    /// <summary>Leading to trailing — a horizontal <c>UIStackView</c> / <c>LinearLayout</c>.</summary>
    Horizontal,
}

/// <summary>How a <see cref="NativeStack" /> aligns its children on the cross axis.</summary>
public enum NativeAlignment
{
    /// <summary>Pack to the leading edge (top for a horizontal stack, leading for a vertical one).</summary>
    Start,

    /// <summary>Centre on the cross axis.</summary>
    Center,

    /// <summary>Pack to the trailing edge.</summary>
    End,

    /// <summary>Fill the cross axis — the default for most layouts.</summary>
    Stretch,
}

/// <summary>A <see cref="NativeLabel" />'s font weight.</summary>
public enum NativeFontWeight
{
    /// <summary>The platform's normal body weight.</summary>
    Regular,

    /// <summary>One step heavier than regular.</summary>
    Medium,

    /// <summary>Between medium and bold — the usual weight for a section heading.</summary>
    Semibold,

    /// <summary>Full bold.</summary>
    Bold,
}

/// <summary>How a <see cref="NativeLabel" /> aligns its text.</summary>
public enum NativeTextAlign
{
    /// <summary>Leading edge — left in a left-to-right locale, right in a right-to-left one.</summary>
    Start,

    /// <summary>Centred.</summary>
    Center,

    /// <summary>Trailing edge.</summary>
    End,
}

/// <summary>A <see cref="NativeButton" />'s visual treatment.</summary>
public enum NativeButtonStyle
{
    /// <summary>A solid, tinted background with contrasting text — the primary action.</summary>
    Filled,

    /// <summary>A soft tinted background — a secondary action.</summary>
    Tinted,

    /// <summary>Text only, no background — a tertiary action.</summary>
    Plain,

    /// <summary>Styled to warn (red on both platforms) — a destructive action.</summary>
    Destructive,
}

/// <summary>How a <see cref="NativeImage" /> scales into its frame.</summary>
public enum NativeContentMode
{
    /// <summary>Scale to fit entirely inside the frame, preserving aspect ratio (letterboxed).</summary>
    Fit,

    /// <summary>Scale to fill the frame, preserving aspect ratio (cropped).</summary>
    Fill,

    /// <summary>No scaling — draw at natural size, centred.</summary>
    Center,
}

/// <summary>Which keyboard a <see cref="NativeTextField" /> raises.</summary>
public enum NativeKeyboardType
{
    /// <summary>The standard text keyboard.</summary>
    Default,

    /// <summary>An email keyboard, with <c>@</c> and <c>.</c> to hand.</summary>
    Email,

    /// <summary>A numeric keypad.</summary>
    Number,

    /// <summary>A phone-number keypad.</summary>
    Phone,

    /// <summary>A URL keyboard, with <c>/</c> and <c>.com</c> to hand.</summary>
    Url,
}
