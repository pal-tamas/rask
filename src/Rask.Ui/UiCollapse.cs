namespace Rask.Ui;

/// <summary>
/// A titled section that opens and closes.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open" /> works exactly as <see cref="UiDropdown.Open" /> does, for the same reason: unset
/// is uncontrolled, and closed writes <c>collapse-close</c> rather than merely omitting
/// <c>collapse-open</c>, because daisyUI also opens on <c>:focus-within</c>.
/// </para>
/// <para>
/// This used to be a <c>&lt;details&gt;</c> with a <c>name</c>, which made a set of them mutually
/// exclusive with no state at all — the browser closed the others when one opened. What it could not do
/// is say WHICH one is open, so a page could neither restore that nor react to it. For a set that
/// behaves as one, <see cref="UiAccordion" /> holds the open key in C#; this is the standalone section.
/// </para>
/// </remarks>
public sealed partial class UiCollapse : Component
{
    /// <summary>daisyUI and MaryUI both call this <c>title</c>. <c>new</c> because the base type carries a
    /// markup entry of that name; this component renders no &lt;title&gt; element, so nothing is lost.</summary>
    public new required string Title { get; set; }

    /// <summary>Whether it is open. Leave it unset to let the browser open it on focus.</summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the heading is activated, with the state the reader is asking for.</summary>
    public Callback<bool>? OnToggle { get; set; }

    /// <summary>Draws the arrow or plus marker.</summary>
    public UiMarker? Marker { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var title = Button
            .Type("button")
            .Class("collapse-title flex w-full items-center text-left font-semibold")
            .Aria(new Dictionary<string, string?> { ["expanded"] = Open == true ? "true" : "false" });

        if (OnToggle is { } toggle)
        {
            var next = Open != true;
            title = title.OnClick(() => toggle.Invoke(next) ?? Task.CompletedTask);
        }

        return Div.Class(UiClass.Compose(
            "collapse border border-base-300 bg-base-100",
            Marker is { } marker ? UiClassNames.Marker(marker) : "",
            Open switch { true => "collapse-open", false => "collapse-close", null => "" },
            Class))[
            title[Title],
            Div.Class("collapse-content text-sm")[Children ?? []]
        ];
    }
}
