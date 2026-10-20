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
                  + "page keeps the sorted column, its direction and the page number, and the table draws them.")
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
                UiDocsExample.Key("ui-table-waits").Heading("Paging that waits").File("TablePagingThatWaitsExample.cs").Demo(TablePagingThatWaitsExample).Testid("ui-table-waits").Nested()
                    .Blurb("Nothing changes until the next page has its rows. Make the read slow and the pager moves "
                           + "first, the rows you have dim, and the new ones take their place when they arrive.")
            ],
            UiTableBehaviour
        ];
}
