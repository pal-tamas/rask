namespace Rask;

public static partial class Ui
{
    /// <summary>
    /// How a row of tabs is drawn.
    /// </summary>
    public enum TabStyle
    {
        /// <summary>Underlined, with no container. The default.</summary>
        Default = 0,

        /// <summary>Each tab a rounded box inside a filled track.</summary>
        Box,

        /// <summary>Separated by a border rather than an underline.</summary>
        Border,

        /// <summary>The active tab lifted into the panel below it, as a folder tab.</summary>
        Lift,
    }
}
