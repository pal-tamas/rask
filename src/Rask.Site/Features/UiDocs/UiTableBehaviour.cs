namespace Rask.Site.Features.UiDocs;

/// <summary>
///     How a table behaves: who sorts and pages, how a slow page change dims its rows, how its columns hold still,
///     its sticky parts and bleed, whose classes win, and where it departs from Flux.
/// </summary>
internal sealed partial class UiTableBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Sorting(),
            Skeleton(),
            Waiting(),
            Steady(),
            Sticky(),
            Classes(),
            Frame()
        ];

    private static UiDocsTopic Sorting() =>
        UiDocsTopic.Key("sorting").Heading("The page sorts and pages").Paragraphs([
            "The table sorts nothing and pages nothing. The page keeps the sorted column, its direction and the page "
            + "number. A `Sortable()` heading draws its label as a button with a chevron, and `OnSort` fires when the "
            + "heading is clicked or its button is pressed from the keyboard; `Sorted` and `Direction` are what the "
            + "page sets in answer.",
            "`Strong` and `Desc` are also HTML tags, and a tag's entry hides a step of the same name: those two values "
            + "are passed as `Variant(Ui.TableCellVariant.Strong)` and `Direction(Ui.TableColumnDirection.Desc)`.",
        ]);

    private static UiDocsTopic Skeleton() =>
        UiDocsTopic.Key("skeleton").Heading("A table drawn from a query shows skeleton rows").Paragraphs([
            "`Ui.Table.Of(orders)` draws the table from a `Rask.Query` query, and the rows stay yours, built from "
            + "`orders.Data`. While it first loads, the headings are there and a skeleton row stands for every row the "
            + "pager says the page holds, each a real row's height. When it found nothing, one row says so "
            + "(`TableEmpty` in the catalog: \"No results.\").",
            "Every later load (a page, a sort, a filter, a tab) shows the skeleton at the press, in the browser, with "
            + "no round trip and no wait: the rows on screen keep their places and their heights and draw a bar in "
            + "place of what they hold, so nothing under the table moves. The new rows take the skeleton's place in "
            + "one step once the page has answered. A pager link and a pager button do the same.",
            "What asks for it: the pager and a sortable heading, a tab above the table, and any control that names the "
            + "table, `.Aria(\"controls\", \"orders\")` beside `Ui.Table.Id(\"orders\")`: a filter field, a Reload "
            + "button. A button inside a row never does. Not Flux's: an addition the owner approved, 2026-10-21. A "
            + "table without `Of` keeps the dimming below.",
        ]);

    private static Component Waiting() =>
        UiDocsTopic.Key("waiting").Heading("A slow page change dims the rows").Paragraphs([
            "Nothing on the table changes until the next page has its rows: a page change, a sort or a filter that is "
            + "back within 150ms is one paint, pager and rows together. When the read is slower the rows the reader has "
            + "stay and dim to half strength until the new ones take their place, for at most two seconds, after which "
            + "they are at full strength again and the new ones arrive when they do. There is nothing to write: "
            + "`Paginate(Ui.Pagination.Paginator(orders).OnPage(async page => { _page = page; _orders = await Orders.Page(page); }))`.",
            "The pager and the headings stay at full strength (they are what says something is happening), the rows "
            + "stay readable and clickable, and a button inside a row, a Delete with a slow handler, marks itself and "
            + "leaves the table alone. No `Loading` prop: the runtime marks what was pressed once it has waited 200ms, "
            + "and the kit's sheet dims the rows by that mark.",
        ])[
            UiDocsKeys.Column("What waits").Rows([
                ("The pager: a page number, Previous, Next, or anything inside it", "`data-loading` on the pager's root, "
                    + "written by `Ui.Pagination` (`data-rask-loading=\"within\"`), under the table or right after it."),
                ("A sortable heading", "`data-loading` and `aria-busy` on its `<th>`, written by "
                                       + "`Ui.TableColumn.Sortable()` (`data-rask-loading`)."),
                ("Anything else: a filter above the table", "`data-loading` on the table, written by you: "
                                                            + "`.Data(\"loading\", \"\")` on the `Ui.Table` while its rows are stale."),
            ]),
            Ui.Text.Class("mt-3")[UiDocsProse.Of(
                "Flux documents no loading treatment for a table. The model is Livewire's own: `data-loading` on what "
                + "was pressed and `has-data-loading:opacity-50` on what it replaces, so a table of your own markup can "
                + "do the same with `has-[[data-loading]]:opacity-50` around anything carrying "
                + "`data-rask-loading=\"within\"`.")],
            Ui.Text.Class("mt-3")[UiDocsProse.Of(
                "Keep the rows: the dim is for rows that are still there. A handler that awaits the read keeps them by "
                + "itself (`_orders = await …` replaces them only when it is back); a page that empties them first "
                + "(`_orders = null`) has nothing to dim, and shows its placeholder when the read is slow. A "
                + "`Rask.Query` page keeps them with `KeepPreviousData`.")]
        ];

    private static UiDocsTopic Steady() =>
        UiDocsTopic.Key("steady").Heading("Columns that stay put").Paragraphs([
            "A table sizes its columns to what is in them, Flux's too, so they shift when a page, a sort or a filter "
            + "brings other values. Give the table a width and each column but one a width, and nothing moves.",
            "`w-full` is what turns the widths on. The table is `table-fixed` already, but a browser lays a table out "
            + "by its content until the table itself has a width, and `min-w-full` is not one. With `w-full` the "
            + "columns come from the heading row alone: the same to the pixel on every page, in every sort order, and "
            + "with no rows.",
            "One column has no width and takes what is left; leave out more than one and they share it equally. "
            + "`truncate` on a cell cuts a long value with an ellipsis where its column ends, and `.Title(text)` puts "
            + "the whole of it in the browser's tooltip. A heading's chevron changes nothing: a sorted column is as "
            + "wide as an unsorted one.",
            "`min-w-160` is for the phone. When the widths add up to more than the screen, the column without one "
            + "would get nothing; with a minimum (40rem: the three widths plus 15rem for the rest) the table keeps its "
            + "columns and scrolls sideways inside its own box, and the page does not. `ContainerClass` (Flux's "
            + "`container:class`) is for a height, `max-h-80`; sideways needs nothing, unless the table is an item of a "
            + "grid or a flex row, which grows to its content: there the box itself becomes 40rem wide and the page "
            + "scrolls instead. Say `.ContainerClass(\"min-w-0\")` and it stays in.",
        ]);

    private static UiDocsTopic Sticky() =>
        UiDocsTopic.Key("sticky").Heading("Sticky parts and bleed").Paragraphs([
            "`Ui.TableColumns.Sticky()` holds the headings at the top of the box, and a sticky `Ui.TableColumn` with "
            + "sticky `Ui.TableCell`s under it holds the first column at the left, or the last at the right. Give each `.Class(\"bg-white "
            + "dark:bg-zinc-900\")`, or the rows show through. A sticky column casts a shadow once the others have "
            + "scrolled under it; that is a CSS scroll timeline, so a browser without one shows no shadow. A sticky "
            + "column takes its width like any other.",
            "`Bleed()` reads `--ui-bleed`, 1.5rem unless the box says otherwise: "
            + "`Div.Class(\"p-4 [--ui-bleed:1rem]\")[Ui.Table.Bleed()[…]]`. A card states it, and its own top and "
            + "bottom bleed, so a table that opens or closes a card reaches that edge too.",
        ]);

    private static UiDocsTopic Classes() =>
        UiDocsTopic.Key("classes").Heading("Flux's classes, and yours with !").Paragraphs([
            "Every part writes Flux's own class list: a cell's padding, text size and colour, a heading's, the table's "
            + "`table-fixed`, `text-zinc-800` and `whitespace-nowrap`. A class of yours that only adds (`truncate`, `w-40`, "
            + "`align-top`, `py-0`, `whitespace-normal` on a cell) does what it says. One that contradicts a default is "
            + "written with Tailwind's `!`, as Flux's guide says: `ps-10!` on a first cell, `dark:text-white/70!`, "
            + "`text-xs!`, `table-auto!`. `colspan` and `scope` go through `.Attributes((\"colspan\", \"3\"))`.",
        ]);

    private static UiDocsTopic Frame() =>
        UiDocsTopic.Key("frame").Heading("The scroll frame holds what is placed in it").Paragraphs([
            "The box the table scrolls in is `relative`, which Flux's is not: a recorded departure from Flux (owner, "
            + "2026-10-14). Flux's frame is static, so an absolutely placed child of a cell, a checkbox's hidden input "
            + "or a hidden heading, escapes it and the whole page scrolls sideways on a phone. Here it stays in the "
            + "frame.",
        ]);
}
