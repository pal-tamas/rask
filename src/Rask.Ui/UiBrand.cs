using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>The surface's mark and name. Goes home.</summary>
/// <remarks>
/// The destination and the wordmark are the caller's, not the kit's — this component used to name the
/// operator console's own overview route and print "Ops", which is exactly the coupling that kept the kit
/// inside one application.
/// </remarks>
public sealed partial class UiBrand : Component
{
    /// <summary>The wordmark. Hidden below <c>sm</c>, so it is never the only thing naming the page.</summary>
    public new required string Label { get; set; }

    /// <summary>Where the mark goes. Home, for whatever this surface calls home.</summary>
    public required RouteUrl Href { get; set; }

    /// <summary>The mark itself. The overview glyph unless said otherwise.</summary>
    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        NavLink
            .Href(Href)
            .Class(
                "flex min-h-11 shrink-0 items-center gap-2 rounded-lg px-1.5 text-sm font-semibold tracking-tight "
                + "text-base-content no-underline hover:bg-base-200 sm:min-h-0 sm:py-1.5")[
            UiIcon.Name(Icon ?? UiIconName.Overview).Class("size-5 shrink-0"),
            // The wordmark is the first thing to go: on a phone the crumb beside it says where you are,
            // which is the part someone actually needs.
            Span.Class("hidden sm:inline")[Label]
        ];
}
