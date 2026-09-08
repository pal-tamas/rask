namespace Rask.Ui;

/// <summary>
/// A group of controls under one caption, with optional help text.
/// </summary>
/// <remarks>
/// A real <c>&lt;fieldset&gt;</c> and <c>&lt;legend&gt;</c> rather than a styled div: it is what gives a
/// screen reader the relationship between the caption and the controls, and what lets a browser disable
/// the whole group at once.
/// </remarks>
public sealed partial class UiFieldset : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    public string? Help { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Fieldset.Class(UiClass.Compose("fieldset", Class))[
            Legend.Class("fieldset-legend")[Title],
            Children ?? [],
            Help is { } help ? P.Class("label")[help] : null
        ];
}
