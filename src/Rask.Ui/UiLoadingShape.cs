namespace Rask.Ui;

/// <summary>
/// What a busy indicator looks like while it spins.
/// </summary>
/// <remarks>
/// Purely cosmetic — every shape says the same thing, and none of them says it to a screen reader. The
/// words beside the indicator are what gets announced; see <see cref="UiLoading" />.
/// </remarks>
public enum UiLoadingShape
{
    /// <summary>A rotating arc. The default.</summary>
    Spinner = 0,

    /// <summary>Three pulsing dots.</summary>
    Dots,

    /// <summary>A closed ring.</summary>
    Ring,

    /// <summary>A bouncing ball.</summary>
    Ball,

    /// <summary>Rising bars.</summary>
    Bars,

    /// <summary>A tracing figure of eight.</summary>
    Infinity,
}
