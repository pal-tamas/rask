namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiCard" /> sets its header, body and footer apart. Flux's <c>body</c>.</summary>
    public enum CardBodyVariant
    {
        /// <summary>Everything shares one surface, separated only by space.</summary>
        Seamless = 0,

        /// <summary>The body is its own panel, a little in from the card's edges.</summary>
        Inset,

        /// <summary>Like <see cref="Inset" />, but the panel runs all the way to the card's sides.</summary>
        Flush,

        /// <summary>Lines separate the header and footer from the body.</summary>
        Divided,

        /// <summary>The header and footer are tinted bands around the body.</summary>
        Separated,
    }
}
