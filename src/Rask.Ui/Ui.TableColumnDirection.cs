namespace Rask;

public static partial class Ui
{
    /// <summary>Which way a sorted <see cref="UiTableColumn" /> runs.</summary>
    public enum TableColumnDirection
    {
        /// <summary>Smallest first: the header shows a chevron pointing up.</summary>
        Asc = 0,

        /// <summary>Largest first: the header shows a chevron pointing down.</summary>
        Desc,
    }
}
