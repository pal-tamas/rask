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
                Pre
                    .Key(index)
                    // The gap after the prompt, as a utility rather than a rule in the kit's sheet.
                    //
                    // daisyUI puts the space there with `margin-right: 2ch` on the pseudo-element, and
                    // then the nested rule that supplies the content REPLACES that declaration block
                    // instead of adding to it - so the margin is lost and the terminal renders `$curl`
                    // with the prompt against the command.
                    //
                    // It cannot be put back with a margin. Tailwind's preflight resets
                    // `*, ::after, ::before { margin: 0; padding: 0 }` and arrives in the APP's
                    // stylesheet; layers do not merge across separate sheets, so that reset outranks
                    // anything layered in the kit's own however specific. WIDTH is untouched by it, and
                    // daisyUI already right-aligns the prompt in a fixed 2rem box - so widening the box
                    // puts the space between prompt and command and leaves the prompt where it was.
                    .Class("before:w-[calc(2rem+2ch)]")
                    .Attributes(("data-prefix", line.Prefix))[Code[line.Text]])
        ];
}
