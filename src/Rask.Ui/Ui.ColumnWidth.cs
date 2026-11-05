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

        /// <summary>A yes or no, or a one-word state: 5rem.</summary>
        Flag,

        /// <summary>A short text that refers to something — a town, a supplier, a category: 10rem.</summary>
        Reference,

        /// <summary>A name beside a longer text that takes the rest: 18rem.</summary>
        Name,

        /// <summary>The row's <see cref="UiRowActions" />, a pencil and a bin: 6rem, at the column's end.</summary>
        Actions,

        /// <summary>A row's end that holds more — three or four icon buttons, or a worded one beside the pencil and bin: 12rem.</summary>
        Buttons,
    }
}
