namespace Rask;

/// <summary>Flux's <c>flux:header</c>: the bar across the top of the page — a brand, the navigation, the account.</summary>
/// <remarks>
/// A sibling of <see cref="UiSidebar" /> and <see cref="UiMain" /> in the layout grid. Written before the
/// sidebar it runs the full width above it; written after, it sits beside it. It paints nothing of its own:
/// the ground and the border are the call site's classes, as in Flux.
/// </remarks>
public sealed partial class UiHeader : Component
{
    /// <summary>Keeps the header at the top of the viewport while the page scrolls under it.</summary>
    public bool? Sticky { get; set; }

    /// <summary>Holds what is in the header to the container width, centred, while its ground runs edge to edge.</summary>
    public bool? Container { get; set; }

    /// <summary>Classes for the header: its ground, its border.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var header = Header.Attributes(("data-ui-header", ""));
        if (Container == true)
        {
            return header.Class(UiClass.Compose("z-10 min-h-14 [grid-area:header]", Sticky == true ? "sticky top-0" : "", Class))[
                Div.Class("mx-auto flex h-14 w-full items-center px-6 lg:px-8 [:where(&)]:max-w-7xl")[Children ?? []]
            ];
        }

        return header.Class(UiClass.Compose(
            "z-10 flex min-h-14 items-center px-6 lg:px-8 [grid-area:header]",
            Sticky == true ? "sticky top-0" : "",
            Class))[
            Children ?? []
        ];
    }
}
