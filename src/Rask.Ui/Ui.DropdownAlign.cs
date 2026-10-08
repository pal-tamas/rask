namespace Rask;

public static partial class Ui
{
    /// <summary>Where along the side it opens on a <see cref="UiDropdown" />'s menu sits.</summary>
    public enum DropdownAlign
    {
        /// <summary>Its start edge on the trigger's start edge. The default.</summary>
        Start,

        /// <summary>Centred on the trigger.</summary>
        Center,

        /// <summary>Its end edge on the trigger's end edge.</summary>
        End,
    }
}
