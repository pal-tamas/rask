namespace Rask;

/// <summary>A form control with its label, description and validation message.</summary>
/// <remarks>
/// Flux UI's field: <c>Ui.Field[Ui.Label["Email"], Ui.Input.Bind(() =&gt; m.Email), Ui.Error]</c>. The field
/// spaces its parts and tells them which control they belong to, so the label reaches it and the error shows
/// its messages. Every kit control also takes <c>Label</c> and <c>Description</c> and draws this around itself.
/// </remarks>
public sealed partial class UiField : Component
{
    // A label over a description sits closer to it; a description under the control keeps the control's distance.
    private const string Block =
        "min-w-0 [&>[data-ui-label]]:mb-3 [&>[data-ui-label]:has(+[data-ui-description])]:mb-2 "
        + "[&>[data-ui-label]+[data-ui-description]]:mt-0 [&>[data-ui-label]+[data-ui-description]]:mb-3 "
        + "[&>*:not([data-ui-label])+[data-ui-description]]:mt-3";

    // Two columns, the narrow one for the control: first when the control comes first, last when the label does.
    private const string Inline =
        "grid gap-x-3 gap-y-1.5 min-w-0 "
        + "has-[>[data-ui-control]~[data-ui-label]]:grid-cols-[auto_1fr] "
        + "has-[>[data-ui-label]~[data-ui-control]]:grid-cols-[1fr_auto] "
        + "[&>[data-ui-control]~[data-ui-description]]:col-start-2 [&>[data-ui-control]~[data-ui-error]]:col-start-2 "
        + "[&>[data-ui-label]~[data-ui-control]]:col-start-2 [&>[data-ui-label]~[data-ui-control]]:row-start-1 "
        + "[&>[data-ui-label]~[data-ui-error]]:col-start-1 [&>[data-ui-error]]:mt-1";

    // The field's own control, not one in a field nested inside it.
    private const string Disabled =
        "[&:not(:has([data-ui-field])):has([data-ui-control][disabled])>[data-ui-label]]:opacity-50";

    /// <summary>Label above the control, or beside it.</summary>
    public Ui.FieldVariant? Variant { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(Variant == Ui.FieldVariant.Inline ? Inline : Block, Disabled, Class))
            .Data("ui-field", "")[
            Context.Provide(UiFieldScope.Of(Children))[Children ?? []]
        ];
}
