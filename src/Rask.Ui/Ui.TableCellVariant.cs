namespace Rask;

public static partial class Ui
{
    /// <summary>How much a <see cref="UiTableCell" /> stands out from the cells beside it.</summary>
    public enum TableCellVariant
    {
        /// <summary>Muted text: what most of a row is.</summary>
        Default = 0,

        /// <summary>Medium weight in the heading colour, for the value a reader scans the column for.</summary>
        Strong,
    }
}
