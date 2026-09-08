namespace Rask.Ui;

/// <summary>
/// A field with a fixed set of answers.
/// </summary>
/// <remarks>
/// A real &lt;select&gt; rather than a styled list, which is what makes it work with a keyboard, a screen
/// reader and a phone's native picker without a line of script.
/// </remarks>
public sealed partial class UiSelect : Component
{
    /// <summary>The accessible name.</summary>
    public required string Label { get; set; }

    /// <summary>The options: the value stored, and the words shown.</summary>
    public required IReadOnlyList<(string Value, string Text)> Options { get; set; }

    public string? Value { get; set; }

    /// <summary>Shown first and unselectable — the prompt, not an answer.</summary>
    public string? Placeholder { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public Action<string>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var select = Select
            .Value(Value ?? string.Empty)
            // aria-invalid is what makes daisyUI reveal a following UiValidator, and what a screen
            // reader needs: a field that is visibly red and says nothing is half a message. It is
            // OMITTED rather than nulled — a null renders the attribute valueless, and a valueless
            // aria-invalid reads as "true", which would mark every field in the kit invalid.
            .Aria(Tone == UiTone.Error
                ? new Dictionary<string, string?> { ["label"] = Label, ["invalid"] = "true" }
                : new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "select validator",
                Tone is { } tone ? UiClassNames.SelectTone(tone) : "",
                Size is { } size ? UiClassNames.SelectSize(size) : "",
                Variant is { } variant ? UiClassNames.SelectVariant(variant) : "",
                Class));

        if (OnChange is { } change)
        {
            select = select.OnChange(change);
        }

        return select[
            // `disabled` as well as empty: a placeholder that can be chosen is an answer, and one chosen
            // by accident is a bug report about a form that saved nothing.
            Placeholder is { } placeholder
                ? Option.Value(string.Empty).Disabled(true).Selected(Value is null)[placeholder]
                : null,
            Options.Select(o => Option.Key(o.Value).Value(o.Value).Selected(o.Value == Value)[o.Text])
        ];
    }
}
