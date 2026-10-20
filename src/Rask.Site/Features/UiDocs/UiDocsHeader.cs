namespace Rask.Site.Features.UiDocs;

/// <summary>How every page of the UI tab opens: its group, its name, what it is, and the Flux page it mirrors.</summary>
internal sealed partial class UiDocsHeader : Component
{
    /// <summary>The page's entry in the catalog, which holds its name and group.</summary>
    public required UiDocsEntry Entry { get; set; }

    /// <summary>The sentence or two under the name.</summary>
    public required string Lead { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mb-10 space-y-3")[
            Ui.Text.Sm.Subtle.Class("font-medium")[Entry.Group],
            Ui.Heading.Level(1).Xxl[Entry.Title],
            Ui.Text.Lg[Lead],
            Entry.FluxUrl is { } flux
                ? Ui.Text.Sm.Subtle[
                    "Drawn and behaving as ",
                    Ui.Link.Href(flux).External()["Flux UI's " + Entry.Title.ToLowerInvariant()],
                    " does, measured against its live docs."
                ]
                : null
        ];
}
