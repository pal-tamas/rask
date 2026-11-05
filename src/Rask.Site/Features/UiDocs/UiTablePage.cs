using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's table and its parts as a C# component: each example with its own code, and its reference.</summary>
[Route("table")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTablePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Table.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Table)
            .Lead("Columns, rows and cells, part for part as Flux's. The table sorts nothing and pages nothing: the "
                  + "page keeps the sorted column, its direction and the page number, and the table draws them — or "
                  + "keeps them in the address for a list page, with SortIn and PageIn.")
            .Api(UiApi.Table)[
            UiDocsExample.Key("ui-table-intro").Heading("Table").File("TableExample.cs").Demo(TableExample).Testid("ui-table-intro")
                .Blurb("A heading row and a row per record. A cell holding a badge drops its own vertical padding."),
            UiDocsExample.Key("ui-table-sortable").Heading("Sortable and paged").File("TableSortableExample.cs").Demo(TableSortableExample).Testid("ui-table-sortable")
                .Blurb("Sortable() headings ask for a sort with OnSort; Paginate puts a Ui.Pagination under the rows. "
                       + "The page holds the column, the direction and the page."),
            UiDocsExample.Key("ui-table-bleed").Heading("Bleed").File("TableBleedExample.cs").Demo(TableBleedExample).Testid("ui-table-bleed")
                .Blurb("The dividers run through the padding of the box the table sits in."),
            UiDocsExample.Key("ui-table-sticky").Heading("Sticky header and column").File("TableStickyExample.cs").Demo(TableStickyExample).Testid("ui-table-sticky")
                .Blurb("Scroll it both ways: the headings and the first column stay put, and the column casts a "
                       + "shadow once the others are under it."),
            UiDocsSection.Key("holding-still").Heading("Tables that hold still")[
                UiDocsExample.Key("ui-table-stable").Heading("Columns that stay put").File("TableSteadyColumnsExample.cs").Demo(TableSteadyColumnsExample).Testid("ui-table-stable").Nested()
                    .Blurb("A width on the table and on every column but one: paging, sorting and an empty list move "
                           + "nothing, and a long value ends in an ellipsis with all of it in its tooltip."),
                UiDocsExample.Key("ui-table-of-query").Heading("Loading from a query").File("TableOfQueryExample.cs").Demo(TableOfQueryExample).Testid("ui-table-of-query").Nested()
                    .Blurb("Of(query) draws the table from a Rask.Query query. Page, sort or switch the tab: the "
                           + "headings stay, the rows turn into skeleton rows of their own size at the press, and the "
                           + "new ones take their place. The read here takes 1.2 s."),
                UiDocsExample.Key("ui-table-waits").Heading("Paging that waits").File("TablePagingThatWaitsExample.cs").Demo(TablePagingThatWaitsExample).Testid("ui-table-waits").Nested()
                    .Blurb("Nothing changes until the next page has its rows. Make the read slow and the pager moves "
                           + "first, the rows you have dim, and the new ones take their place when they arrive.")
            ],
            UiDocsExample.Key("ui-table-fold-out").Heading("A row that folds out").File("TableFoldOutExample.cs").Demo(TableFoldOutExample).Testid("ui-table-fold-out")
                .Blurb("The chevron folds a row of its own out beneath a pump's: one cell across all three columns, "
                       + "Ui.TableCell.ColSpan(3), as Flux's cell takes colspan. Ui.Empty's row spans the same way."),
            UiDocsExample.Key("ui-table-widths").Heading("Column widths").File("TableColumnWidthsExample.cs").Demo(TableColumnWidthsExample).Testid("ui-table-widths")
                .Blurb("Width(Ui.ColumnWidth.Day) and the rest say what each column holds, and that alone sizes it: the table "
                       + "fills its box, never shrinks below its columns together, and a phone scrolls it. Numbers and the row "
                       + "actions keep to the end. Cut(text) ends a long value in an ellipsis with all of it in its tooltip. "
                       + "Not Flux's: the kit's own."),
            UiDocsExample.Key("ui-table-list").Heading("A list page").File("TableListPageExample.cs").Demo(TableListPageExample).Testid("ui-table-list")
                .Blurb("SortIn and PageIn keep the sort and the page in the address: sort, page, then copy the URL. Delete "
                       + "the last row of the last page and the list steps back a page. Empty says \"nothing yet\" of an "
                       + "empty list, and \"nothing matches\" while the search narrows it."),
            UiTableBehaviour
        ];
}
