namespace Rask.Ui;

/// <summary>
/// A line between two parts of a page, optionally with a word on it — Flux UI's separator.
/// </summary>
/// <remarks>
/// <para>
/// <b>No outer margin.</b> daisyUI gives a divider a built-in 1rem margin on both sides of its line, which is the
/// kind of spacing a component should not decide: the same line sits between two fields, between groups in a
/// menu, and in a toolbar, and one margin is wrong for two of them. The kit's stylesheet zeroes it, so a divider is
/// exactly as tall as its line and its word, and the page spaces it — <c>.Class("my-4")</c>.
/// </para>
/// <para>
/// A <c>separator</c> to assistive tech, with its orientation, when it is a plain line; a divider carrying words is
/// left as text, because a separator's content is not read.
/// </para>
/// </remarks>
public sealed partial class UiDivider : Component
{
    /// <summary>The word on the line — "or", "then". Omitted, it is a plain rule.</summary>
    public new string? Text { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>Runs down instead of across. daisyUI's <c>divider-horizontal</c>.</summary>
    public bool? Vertical { get; set; }

    /// <summary>A fainter line — Flux's <c>variant="subtle"</c>, for a divider inside a toolbar or a menu.</summary>
    public bool? Subtle { get; set; }

    /// <summary>
    ///     Moves the word to one end: <see cref="UiAlign.Start" /> or <see cref="UiAlign.End" />. Centred unless this
    ///     says otherwise.
    /// </summary>
    public UiAlign? Align { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var line = Div.Class(UiClass.Compose(
            "divider",
            Vertical == true ? "divider-horizontal" : "",
            Tone is { } tone ? UiClassNames.DividerTone(tone) : "",
            Subtle == true ? "ui-divider-subtle" : "",
            Align is { } align ? UiClassNames.DividerAlign(align) : "",
            Class));

        if (Text is null)
        {
            line = line.Role("separator");
            if (Vertical == true)
            {
                line = line.Aria("orientation", "vertical");
            }
        }

        return line[Text];
    }
}
