namespace Rask;

public static partial class Ui
{
    // Flux's subtle small text (flux:text variant="subtle" size="sm"), and its body text (flux:text).
    private const string FactLabel = "text-xs text-zinc-400 dark:text-white/50";

    private const string FactValue = "m-0 mt-0.5 min-w-0 break-words text-sm text-zinc-500 dark:text-white/70";

    // What a fact with no value says: a dash, so the label never stands over nothing.
    private const string NoValue = "–";

    /// <summary>
    ///     One fact of a <see cref="UiFacts" />: its label over its value, the <c>&lt;dt&gt;</c> and <c>&lt;dd&gt;</c> in a
    ///     <c>&lt;div&gt;</c>, as a description list may group them. <c>Ui.Fact("Company", driver.Company)</c>.
    /// </summary>
    /// <remarks>
    ///     A value is any markup — words, a number, a badge, a link. None, or only blanks, shows a dash. The fact hands
    ///     back its <c>&lt;div&gt;</c>, so a long one can take more room: <c>Ui.Fact("Note", note).Class("col-span-full")</c>.
    /// </remarks>
    /// <param name="label">What the value is: the term.</param>
    /// <param name="value">The value, under the label; none shows a dash.</param>
    /// <returns>The fact's <c>&lt;div&gt;</c>, to place in a <see cref="UiFacts" />.</returns>
    public static HTMLDivElement Fact(string label, Component? value)
    {
        ArgumentNullException.ThrowIfNull(label);

        // The indexer hands back the element it was given as a Component; the div is kept to hand back typed.
        var fact = Markup.Div.Data("ui-fact");
        _ = fact[
            Markup.Dt.Class(FactLabel)[label],
            Markup.Dd.Class(FactValue)[IsBlank(value) ? NoValue : value]
        ];
        return fact;
    }

    private static bool IsBlank(Component? value) =>
        value is null || value is Text { Value: var text } && string.IsNullOrWhiteSpace(text);
}
