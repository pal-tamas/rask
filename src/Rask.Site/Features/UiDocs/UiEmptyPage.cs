using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's empty state — not a Flux component, the one Flux's own demo draws — its examples with their code, and its
///     reference.
/// </summary>
[Route("empty")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiEmptyPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Empty.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Empty)
            .Lead("What a list says when it has nothing to show: one sentence, centred, with room above and below, said to "
                  + "a screen reader as it changes. Flux UI has no empty state of its own; this is the one its account "
                  + "overview demo draws when a search matches nothing, measured from it.")
            .Api(UiApi.Empty)[
            UiDocsExample.Key("ui-empty-row").Heading("In a table").File("EmptyExample.cs").Demo(EmptyExample).Testid("ui-empty-row")
                .Blurb("Search for what no supplier is called. The table keeps its headings and its one row says so, its "
                       + "cell across both columns: Ui.TableCell.ColSpan(2)[Ui.Empty[…]]. A table drawn from a query does "
                       + "this itself: Ui.Table.Of(suppliers).Empty(\"No suppliers yet.\", \"No supplier matches.\")."),
            UiDocsExample.Key("ui-empty-list").Heading("In place of a list").File("EmptyListExample.cs").Demo(EmptyListExample).Testid("ui-empty-list")
                .Blurb("A list with nothing in it yet says so where its table would be. Ui.Empty has no ink of its own — in a "
                       + "cell it reads in the cell's — so out of one the page gives it: the cell's 14px muted ink here.")
        ];
}
