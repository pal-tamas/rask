namespace Rask.Ui;

/// <summary>
/// One slot of text, cycling through several words.
/// </summary>
/// <remarks>
/// <para>
/// <b>The speed is a Tailwind duration utility, not a property here</b>, and that is a constraint rather
/// than a preference. daisyUI reads the cycle length from <c>--tw-duration</c>, which is set by a
/// <c>duration-*</c> class; a <c>TimeSpan</c> property would have to turn a number into a class name at
/// run time, and a name built that way is invisible to Tailwind's scan, absent from the sheet, and
/// silently ignored. Pass <c>duration-[3s]</c> (or any <c>duration-*</c>) through <see cref="Class" />
/// and it will be in the sheet because it is written down. Unset, daisyUI uses ten seconds.
/// </para>
/// <para>
/// All the words are in the markup, so a reader who never sees the animation reads the list. That also
/// means the phrase has to make sense with every one of them: this rotates a word, it does not rewrite
/// a sentence.
/// </para>
/// <para>
/// It respects <c>prefers-reduced-motion</c> — daisyUI steps between the words rather than sliding —
/// which is the browser's, not something a call site has to remember.
/// </para>
/// </remarks>
public sealed partial class UiTextRotate : Component
{
    /// <summary>The words to cycle. daisyUI lays out up to eight.</summary>
    public required IReadOnlyList<string> Words { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class(UiClass.Compose("text-rotate", Class))[
            // The inner wrapper is daisyUI's `> *`: it becomes the grid that scrolls, and counts ITS
            // children to pick the animation. Flattening this away leaves the words unanimated.
            Span[
                Words.Select(word => Span.Key(word)[word])
            ]
        ];
}
