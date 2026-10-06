namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiField" /> lays out its label, control and messages.</summary>
    public enum FieldVariant
    {
        /// <summary>One above the other: label, control, error, description.</summary>
        Block = 0,

        /// <summary>The label beside the control — a checkbox, a radio, a switch.</summary>
        Inline,
    }
}
