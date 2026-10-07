namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Which side of its heading a <see cref="UiAccordion" /> draws the chevron on.
    /// </summary>
    public enum AccordionVariant
    {
        /// <summary>After the heading, pointing down and turning up as the item opens.</summary>
        Default,

        /// <summary>Before the heading, pointing right and turning down as the item opens.</summary>
        Reverse,
    }
}
