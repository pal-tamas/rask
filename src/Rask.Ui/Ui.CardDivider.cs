namespace Rask;

public static partial class Ui
{
    /// <summary>Where a <see cref="CardBodyVariant.Divided" /> card's lines stop. Flux's prop of the same name.</summary>
    public enum CardDivider
    {
        /// <summary>Across the whole card.</summary>
        Full = 0,

        /// <summary>At the content's edges.</summary>
        Inset,
    }
}
