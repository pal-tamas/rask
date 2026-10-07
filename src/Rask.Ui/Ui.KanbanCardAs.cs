namespace Rask;

public static partial class Ui
{
    /// <summary>The element a <see cref="UiKanbanCard" /> is.</summary>
    public enum KanbanCardAs
    {
        /// <summary>A <c>&lt;div&gt;</c>: something to read. The default.</summary>
        Div,

        /// <summary>A <c>&lt;button type="button"&gt;</c>: the whole card is pressed.</summary>
        Button,
    }
}
