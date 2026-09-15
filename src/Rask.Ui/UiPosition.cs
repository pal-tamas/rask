namespace Rask.Ui;

/// <summary>
/// Which side of its anchor a floating part opens on.
/// </summary>
/// <remarks>
/// <para>
/// One of the two words every floating part in the kit is placed with, and the same two Flux UI uses:
/// <c>Position</c> picks the SIDE, <see cref="UiAlign" /> slides the part along that side. They are two
/// properties rather than one enum because daisyUI composes them — <c>dropdown-top dropdown-end</c> is a
/// menu above its trigger, flush with the trigger's end edge — and a single enum can only name one of the
/// pair, which is how the old <c>UiPlacement</c> came to mix sides and edges and offer neither whole.
/// </para>
/// <para>
/// Not every component honours every side: a row of tabs sits only above or below its panel, and a drawer
/// only at the left or the right. A side a component has no class for writes nothing rather than a class
/// that styles nothing.
/// </para>
/// </remarks>
public enum UiPosition
{
    /// <summary>The component's own default, whatever daisyUI's theme says that is.</summary>
    Default = 0,

    /// <summary>Above.</summary>
    Top,

    /// <summary>To the right, regardless of reading direction.</summary>
    Right,

    /// <summary>Below.</summary>
    Bottom,

    /// <summary>To the left, regardless of reading direction.</summary>
    Left,
}
