namespace Rask.Ui;

/// <summary>
/// A terminal, for showing a command.
/// </summary>
/// <remarks>
/// Each line is a <c>&lt;pre&gt;</c> with a <c>data-prefix</c>, which is how daisyUI draws the prompt
/// character — it is a CSS pseudo-element, so it is decoration and is never copied when a reader selects
/// the command. That is the point: a prompt that comes along with the copied text is a broken paste.
/// </remarks>
public sealed partial class UiMockupCode : Component
{
    /// <summary>The lines, each with the prefix daisyUI draws before it ("$", ">", "1").</summary>
    public required IReadOnlyList<(string Prefix, string Text)> Lines { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    // overflow-x-auto: daisyUI gives .mockup-code `overflow: hidden` for its rounded corners, and each
    // line is a <pre> with `white-space: pre` and `min-width: 100%`. A line longer than the box is
    // therefore CLIPPED, with no scrollbar and nothing to drag — on rask.sh's landing page that cut
    // `curl -sSL https://rask.sh/rask.sh | sh` off after the URL on every phone, which is the one line
    // on the page a reader is there to copy. A terminal scrolls sideways; it does not wrap, because a
    // wrapped shell command is ambiguous about where it ends.
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-code overflow-x-auto", Class))[
            Lines.Select((line, index) =>
                Pre.Key(index).Attributes(("data-prefix", line.Prefix))[Code[line.Text]])
        ];
}
