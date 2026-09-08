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
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-code", Class))[
            Lines.Select((line, index) =>
                Pre.Key(index).Attributes(("data-prefix", line.Prefix))[Code[line.Text]])
        ];
}
