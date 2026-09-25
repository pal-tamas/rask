namespace Rask;

/// <summary>
/// A titled group of <see cref="UiNavItem" />s in a <see cref="UiNavList" />, optionally one that folds away.
/// </summary>
/// <remarks>
/// <para>
/// Plain, it is a heading over its items. <see cref="Expandable" /> makes it daisyUI's collapsible
/// <c>&lt;details&gt;</c> group, which opens and closes with no runtime and is open unless <see cref="Expanded" />
/// says otherwise.
/// </para>
/// <para>
/// <see cref="Expanded" /> with <see cref="OnToggle" /> hands the open state to the page — a navigation that opens the
/// group holding the current page, or a filter that opens every group with a match.
/// </para>
/// </remarks>
public sealed partial class UiNavGroup : Component
{
    /// <summary>The group's title.</summary>
    public required string Heading { get; set; }

    public Ui.IconName? Icon { get; set; }

    /// <summary>Folds the items away under the heading.</summary>
    public bool? Expandable { get; set; }

    /// <summary>Whether an expandable group is open. Open unless this is <see langword="false" />.</summary>
    public bool? Expanded { get; set; }

    /// <summary>Runs when the reader opens or closes an expandable group, with the state it is now in.</summary>
    public Callback<bool> OnToggle { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component heading =
        [
            Icon is { } icon ? Ui.Icon.Name(icon).Class("size-4 shrink-0") : null!,
            // ui-rail-hide: the heading goes when a collapsable sidebar is narrowed to its rail; an icon, if the
            // group has one, is what is left to say which group this is.
            Span.Class("ui-rail-hide")[Heading]
        ];

        if (Expandable != true)
        {
            return Li.Class(Class)[
                Div.Class("menu-title flex items-center gap-2")[heading],
                Ul[Children ?? []]
            ];
        }

        var details = Details.Open(Expanded != false);
        if (OnToggle.HasValue)
        {
            details = details.OnToggle(e => OnToggle.Invoke(e.IsOpen).AsTask());
        }

        return Li.Class(Class)[
            details[
                Summary[heading],
                Ul[Children ?? []]
            ]
        ];
    }
}
