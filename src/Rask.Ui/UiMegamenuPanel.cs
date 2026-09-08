namespace Rask.Ui;

/// <summary>
/// One trigger of a <see cref="UiMegamenu" /> and the panel it opens.
/// </summary>
/// <remarks>
/// <see cref="Id" /> joins the two halves through <c>popovertarget</c>, so it has to be unique on the
/// page — two panels sharing one would give the first two triggers and the second none.
/// </remarks>
public sealed partial class UiMegamenuPanel : Component
{
    /// <summary>The label on the trigger.</summary>
    public required string Trigger { get; set; }

    /// <summary>Unique on the page. It is what the trigger names.</summary>
    public required string Id { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // Two roots and no wrapper between them: the trigger has to be a sibling of the other triggers
        // and the panel a sibling of the other panels, or daisyUI's nth-of-type anchoring misnumbers.
        [
            Button.Type("button").Attributes(("popovertarget", Id))[Trigger],
            // No class of its own. daisyUI styles the panel through `.megamenu [popover]`, so a name
            // invented here would style nothing while looking as though it did.
            Div.Id(Id).Class(Class).Popover("auto")[Children ?? []]
        ];
}
