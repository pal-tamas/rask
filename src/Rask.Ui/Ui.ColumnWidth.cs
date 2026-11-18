namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     What a <see cref="UiTableColumn" /> holds, which alone decides how wide it is: a list whose every heading says
    ///     one keeps its columns where they are while a page, a sort or a filter changes its rows.
    /// </summary>
    /// <remarks>
    ///     Not Flux's: an addition the owner approved (2026-11-04, #1298). A table with one is as wide as its box and never
    ///     narrower than its columns together (40rem at least), so a phone scrolls it inside its box. On paper the widths
    ///     give way: every column is printed on the page.
    /// </remarks>
    public enum ColumnWidth
    {
        /// <summary>A name, or the longest text of the row: the column that takes what the others leave (12rem at least).</summary>
        Rest = 0,

        /// <summary>A day, "2026. 01. 15.": 7rem.</summary>
        Day,

        /// <summary>A day with its time, "2026. 01. 15. 13:45": 10rem.</summary>
        Moment,

        /// <summary>An amount, a quantity, a count: 7rem, at the column's end.</summary>
        Number,

        /// <summary>A short code — a plate, a tax number, an invoice number, "GB 123 4567 89": 10rem.</summary>
#pragma warning disable CA1720 // The owner's name for the step (`Ui.TableColumn.Short`, 2026-11-05): a short code, not the C# `short`.
        Short,
#pragma warning restore CA1720

        /// <summary>A code — an invoice number, a plate, a product code, "INV-26-0042": 8rem.</summary>
        /// <remarks>
        ///     Two rem narrower than <see cref="Short" />, for a code that is never spaced out: a plate or an invoice
        ///     number in the kit's 14px text, cut short where it is longer. An owner-approved addition (2026-11-18, #1371).
        /// </remarks>
        Code,

        /// <summary>A yes or no, or a one-word state: 5rem.</summary>
        Flag,

        /// <summary>A short text that refers to something — a town, a supplier, a category: 10rem.</summary>
        Reference,

        /// <summary>A name beside a longer text that takes the rest: 18rem.</summary>
        Name,

        /// <summary>
        ///     A row's end that holds two icon buttons of the page's own — a pencil and a bin: 6rem, at the column's end.
        /// </summary>
        /// <remarks>
        ///     Measured on Flux's icon buttons, not guessed: two of the size a table row takes (<c>Ui.Button.Sm</c>, 2rem
        ///     square), a gap (<c>gap-1</c>) apart, after the cell's 0.75rem start padding, need 5rem, and 5.75rem where the
        ///     column is not the row's last and keeps its end padding too. 6rem holds either, and two of the default size
        ///     (2.5rem square) as the row's last column. An owner-approved addition (2026-11-18, #1358).
        /// </remarks>
        Actions,

        /// <summary>A row's end that holds buttons of the page's own — three or four icon buttons, or a worded one: 12rem.</summary>
        Buttons,

        /// <summary>
        ///     The row's <see cref="UiRowActions" />, behind one ⋯: 4rem, at the column's end,
        ///     and held at the table's right edge while the other columns scroll under it, as its cells are.
        /// </summary>
        RowMenu,
    }
}
