namespace Rask.Ui;

/// <summary>
/// What a piece of the kit is saying about the thing it shows: its COLOUR.
/// </summary>
/// <remarks>
/// <para>
/// These are daisyUI's semantic colours, named as daisyUI names them. Translating them into a private
/// vocabulary was the first thing this kit did and the first thing it stopped doing: a caller reading
/// daisyUI's documentation, which is the documentation for everything the components render, would have
/// had to map every example back through a second set of words for no gain.
/// </para>
/// <para>
/// Colour is one axis of three, and they compose. <see cref="UiVariant" /> chooses how a component is
/// drawn — filled, outlined, ghosted — and <see cref="UiSize" /> how big. What used to be
/// <c>Quiet</c> is <see cref="UiVariant.Ghost" />, because it was never a colour.
/// </para>
/// <para>
/// Not every component honours every member, and each documents what it does with the rest; anything a
/// component has no meaning for is treated as <see cref="Neutral" />.
/// </para>
/// </remarks>
public enum UiTone
{
    /// <summary>The ordinary weight. Says nothing in particular.</summary>
    Neutral = 0,

    /// <summary>The one action a surface most expects to be taken.</summary>
    Primary,

    /// <summary>The supporting action beside it.</summary>
    Secondary,

    /// <summary>A third emphasis, for surfaces that need one.</summary>
    Accent,

    /// <summary>Worth reading, and not a problem. Also what "working right now" reads as.</summary>
    Info,

    /// <summary>Healthy, finished, succeeded.</summary>
    Success,

    /// <summary>Not wrong yet — unproven, stale, or approaching a limit.</summary>
    Warning,

    /// <summary>Failed, or about to destroy something.</summary>
    Error,
}
