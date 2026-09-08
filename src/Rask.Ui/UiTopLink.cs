using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>A link out of the console, in the top bar's trailing edge.</summary>
public sealed partial class UiTopLink : Component
{
    public required string Label { get; set; }

    public required string Href { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // A plain anchor, not a NavLink: this leaves the application, so it must be a browser navigation
        // rather than a live one. rel/target because it is a documentation site, not part of the console.
        A.Href(Href)
            .Target("_blank")
            .Rel("noopener noreferrer")
            .Class(
                "flex min-h-11 items-center rounded-lg px-2 text-sm opacity-60 no-underline "
                + "hover:bg-base-200 hover:text-base-content sm:min-h-0 sm:py-1.5")[
            Label
        ];
}
