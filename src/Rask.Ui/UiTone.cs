namespace Rask.Ui;

/// <summary>
/// What a piece of the kit is saying about the thing it shows.
/// </summary>
/// <remarks>
/// <para>
/// An enum rather than the string this was while the kit had one caller inside the operator console.
/// A string tone has no discoverable set of values and no wrong value — every misspelling silently
/// selects the neutral branch, which reads on screen as "that component ignored me". As public API
/// that is a trap; the compiler should be the thing that knows the vocabulary.
/// </para>
/// <para>
/// Not every component honours every member — a status dot has no <see cref="Primary" />, a button no
/// <see cref="Busy" />. Each one documents what it does with the rest, and treats anything it has no
/// meaning for as <see cref="Neutral" />, which is the same thing it did before.
/// </para>
/// </remarks>
public enum UiTone
{
    /// <summary>The ordinary weight. Says nothing in particular.</summary>
    Neutral = 0,

    /// <summary>The one action a surface most expects to be taken.</summary>
    Primary,

    /// <summary>Present, but not competing for attention.</summary>
    Quiet,

    /// <summary>Healthy, finished, succeeded.</summary>
    Ok,

    /// <summary>Not wrong yet — unproven, stale, or approaching a limit.</summary>
    Warn,

    /// <summary>Failed, or about to destroy something.</summary>
    Danger,

    /// <summary>Working right now.</summary>
    Busy,
}
