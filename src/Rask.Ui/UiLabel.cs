namespace Rask;

/// <summary>The name of a form control: <c>Ui.Label["Email"]</c>.</summary>
/// <remarks>
/// Flux UI's label, as a real <c>&lt;label&gt;</c>: inside a <see cref="UiField" /> it points at the field's
/// control, so clicking it focuses the control with no script. Outside one, name the control with
/// <see cref="For" />.
/// </remarks>
public sealed partial class UiLabel : Component
{
    private const string Look = "items-center text-sm font-medium text-zinc-800 dark:text-white cursor-default";

    private const string BadgeLook =
        "ms-1.5 -my-1 rounded-sm px-1.5 py-1 text-xs font-medium "
        + "text-zinc-800/70 bg-zinc-800/5 dark:text-zinc-300 dark:bg-white/10";

    /// <summary>A short word beside the text — <c>"Required"</c>, <c>"Optional"</c>.</summary>
    public string? Badge { get; set; }

    /// <summary>Content at the far end of the label: a value, a link.</summary>
    public Component? Trailing { get; set; }

    /// <summary>The id of the control it names. Inside a field this is the field's control.</summary>
    public string? For { get; set; }

    /// <inheritdoc cref="Element.Id" />
    public string? Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiFieldScope>();
        var label = Label.Id(Id ?? scope?.LabelId)
            .Class(UiClass.Compose(Trailing is null ? "inline-flex" : "flex", Look, Class))
            .Data("ui-label", "");

        if ((For ?? scope?.ControlId) is { } control)
        {
            label = label.For(control);
        }

        return label[
            Children ?? [],
            // Hidden from assistive tech: the label is the control's name, and "Email Required" is not its name.
            Badge is null ? null : Span.Class(BadgeLook).Aria("hidden", "true")[Badge],
            Trailing is null ? null : Div.Class("ms-auto").Data("ui-label-trailing", "")[Trailing]
        ];
    }
}
