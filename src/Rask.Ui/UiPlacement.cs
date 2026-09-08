namespace Rask.Ui;

/// <summary>
/// Which side of its anchor a floating part opens on.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI spells placement per component — <c>dropdown-end</c>, <c>tooltip-top</c> — over the same
/// small vocabulary, so one enum serves them all rather than each component inventing its own set of
/// strings. It replaces the free-form <c>Placement</c> properties the kit used to carry: a misspelled
/// class name is not a compile error, and the failure it causes is silent, because a class daisyUI
/// never defined simply does not style anything.
/// </para>
/// <para>
/// Not every component honours every member — a tooltip has no <see cref="Start" /> and a dropdown no
/// <see cref="Center" /> of its own — and anything a component has no form for falls back to its own
/// default placement rather than writing a class that styles nothing.
/// </para>
/// </remarks>
public enum UiPlacement
{
    /// <summary>The component's own default, whatever daisyUI's theme says that is.</summary>
    Default = 0,

    /// <summary>Aligned to the reading-start edge.</summary>
    Start,

    /// <summary>Centred on its anchor.</summary>
    Center,

    /// <summary>Aligned to the reading-end edge.</summary>
    End,

    /// <summary>Above.</summary>
    Top,

    /// <summary>Below.</summary>
    Bottom,

    /// <summary>To the left, regardless of reading direction.</summary>
    Left,

    /// <summary>To the right, regardless of reading direction.</summary>
    Right,
}
