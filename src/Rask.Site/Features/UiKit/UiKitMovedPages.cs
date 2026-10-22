using Rask.Site.Features.UiDocs;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Where a family page's components went: each has a page of its own in the UI tab now, with its examples and its
///     reference, and the family page keeps only what has not moved yet.
/// </summary>
internal sealed partial class UiKitMovedPages : Component
{
    /// <summary>The pages that took this page's components, in the order they used to appear here.</summary>
    public required IReadOnlyList<UiDocsEntry> Pages { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("my-6").Data("testid", "ui-moved")[
            Ui.Callout
                .Icon(Ui.IconName.ArrowRightCircle)
                .Heading("These have their own pages in the UI tab")
                .Text("Each with every example and a reference of its steps.")
                // One wrapping row: nine pages are wider than a phone, and the callout's own actions row does not wrap.
                .Actions([
                    Div.Class("flex flex-wrap gap-2")[
                        Pages.Select(page => Ui.Button.Sm.Key(page.Title).Href(PageMeta.LinkTo(page.Path))[page.Title])
                    ],
                ])
        ];
}
