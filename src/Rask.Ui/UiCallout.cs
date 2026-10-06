namespace Rask;

/// <summary>
///     Flux's <c>flux:callout</c>: something the page needs its reader to notice, in place — an icon, a
///     heading, a line or two of text, and optionally what to do about it.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Heading("Payment failed")</c> is the short form;
///     <see cref="UiCalloutHeading" />, <see cref="UiCalloutText" /> and <see cref="UiCalloutLink" /> as
///     children are the long one, and anything else passed as a child sits under them.
///     </para>
///     <para>
///     It announces nothing by itself, exactly as Flux's does not: a callout that is on the page when it
///     loads is content, and a live region there would be read out over the page's own heading. One that
///     APPEARS because something happened says so at the call site — <c>.Role("alert")</c> for a failure,
///     <c>.Role("status")</c> for news that can wait.
///     </para>
///     <para>
///     It is a container (<c>@container</c>): below 28rem of its own width an <see cref="Inline" />
///     callout stacks its actions under the text again, whatever the viewport is.
///     </para>
/// </remarks>
public sealed partial class UiCallout : Component
{
    private const string Frame = "@container flex p-2 border rounded-xl";

    private const string Content = "flex-1 flex flex-col justify-center gap-2 py-2 pe-3 @md:pe-4";

    private const string ActionsBelow = "flex items-center gap-2 self-start py-2";

    // Beside the text once there is room, the first action outermost; level with a lone heading, and
    // padded like the content when there is text under it.
    private const string ActionsBeside =
        ActionsBelow
        + " @md:flex-row-reverse @md:justify-end @md:-m-0.5 @md:py-0 @md:ps-4"
        + " @md:[[data-ui-callout]:has([data-slot=text])_&]:p-2";

    private static readonly IReadOnlyDictionary<string, string?> Marker = UiDataMarker.Of("ui-callout");

    private static readonly Dictionary<string, string?> ContentSlot = new(StringComparer.Ordinal) { ["slot"] = "content" };

    private static readonly Dictionary<string, string?> ActionsSlot = new(StringComparer.Ordinal) { ["slot"] = "actions" };

    /// <summary>The icon beside the content, at the top. None when unset.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Which drawing of <see cref="Icon" />. <see cref="Ui.IconVariant.Mini" /> when unset.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>An icon of your own in place of <see cref="Icon" />: Flux's <c>icon</c> slot.</summary>
    public Component? CustomIcon { get; set; }

    /// <summary>
    ///     What it is saying. Unset, it is drawn as <see cref="Ui.CalloutVariant.Secondary" /> on a white
    ///     surface instead of a zinc one, which is what Flux draws for a callout given no variant.
    /// </summary>
    public Ui.CalloutVariant? Variant { get; set; }

    /// <summary>A hue of its own, which wins over <see cref="Variant" />. Flux draws one grey, so every grey is zinc.</summary>
    public Ui.Color? Color { get; set; }

    /// <summary>Puts <see cref="Actions" /> beside the text instead of under it, where there is room.</summary>
    public bool? Inline { get; set; }

    /// <summary>Shorthand for a <see cref="UiCalloutHeading" /> as the first child.</summary>
    public string? Heading { get; set; }

    /// <summary>Shorthand for a <see cref="UiCalloutText" /> after the heading.</summary>
    public string? Text { get; set; }

    /// <summary>What the reader can do about it: buttons or links, under the text or beside it (<see cref="Inline" />).</summary>
    public Component? Actions { get; set; }

    /// <summary>What sits at the top right — a dismiss button. Dismissing is the app's: the callout only places it.</summary>
    public Component? Controls { get; set; }

    /// <summary>The <c>id</c> of the callout.</summary>
    public string? Id { get; set; }

    /// <summary>The <c>role</c> of the callout. None when unset: see the remarks on <see cref="UiCallout" />.</summary>
    public string? Role { get; set; }

    /// <summary>Classes for the call site, added to the callout's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Id(Id).Class(UiClass.Compose(Frame, Palette(), Class)).Data(Marker).Role(Role)[
            IconColumn(),
            Div.Class(Inline == true ? "flex-1 ps-2 @md:flex" : "flex-1 ps-2")[
                Div.Class(Content).Data(ContentSlot)[
                    Heading is null ? null : Ui.CalloutHeading[Heading],
                    Text is null ? null : Ui.CalloutText[Text],
                    Children ?? []
                ],
                ActionsRow()
            ],
            ControlsColumn()
        ];

    private Component? ActionsRow()
    {
        if (Actions is null)
        {
            return null;
        }

        return Div.Class(Inline == true ? ActionsBeside : ActionsBelow).Data(ActionsSlot)[Actions];
    }

    private Component? ControlsColumn()
    {
        if (Controls is null)
        {
            return null;
        }

        // Pulled out by the 2px the actions are, so a 40px button stands level with them in a 36px row.
        return Div.Class(Inline == true ? "ps-2 -m-0.5" : "ps-2")[Controls];
    }

    private string Palette() => (Color, Variant) switch
    {
        ({ } color, _) => UiCalloutPalette.For(color),
        (_, { } variant) => UiCalloutPalette.For(variant),
        _ => UiCalloutPalette.Plain,
    };

    // Baseline-aligned in a column padded like the content, so the icon sits on the heading's first line.
    private Component? IconColumn()
    {
        var icon = CustomIcon ?? (Icon is { } name ? Ui.Icon.Name(name).Variant(IconVariant ?? Ui.IconVariant.Mini) : null);

        return icon is null ? null : Div.Class("flex items-baseline py-2 ps-2")[icon];
    }
}
