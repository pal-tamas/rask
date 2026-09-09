using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>One section tab.</summary>
public sealed partial class UiNavTab : Component
{
    public new required string Label { get; set; }

    public required RouteUrl Href { get; set; }

    public bool? Active { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var active = Active == true;

        var tab = NavLink
            .Href(Href)
            // -mb-px pulls the tab's own bottom border onto the bar's, so the active underline replaces the
            // hairline rather than sitting above it.
            .Class(
                "-mb-px flex min-h-11 shrink-0 items-center whitespace-nowrap border-b-2 pb-2.5 pt-2.5 text-sm "
                + "no-underline " + (active
                    ? "border-ui-ink font-medium text-base-content"
                    : "border-transparent opacity-60 hover:border-base-300 hover:text-base-content"));

        // Added only when it is true, rather than as one half of a ternary that has to yield a tuple either
        // way. The else branch of that shape ships a meaningless data-inactive on every inactive tab of
        // every page, and invites someone to start styling off it.
        if (active)
        {
            tab = tab.Attributes(("aria-current", "page"));
        }

        return tab[Label];
    }
}
