namespace Rask;

public static partial class Ui
{
    /// <summary>
    /// A hue of Tailwind's palette, for a component whose <c>Color</c> names one.
    /// </summary>
    /// <remarks>
    /// Tailwind's names in Tailwind's order. Which shade of the hue is drawn — and which in dark — is the
    /// component's to say; a component that takes no colour is drawn in the accent.
    /// </remarks>
    public enum Color
    {
        /// <summary><c>red</c>.</summary>
        Red = 0,

        /// <summary><c>orange</c>.</summary>
        Orange,

        /// <summary><c>amber</c>.</summary>
        Amber,

        /// <summary><c>yellow</c>.</summary>
        Yellow,

        /// <summary><c>lime</c>.</summary>
        Lime,

        /// <summary><c>green</c>.</summary>
        Green,

        /// <summary><c>emerald</c>.</summary>
        Emerald,

        /// <summary><c>teal</c>.</summary>
        Teal,

        /// <summary><c>cyan</c>.</summary>
        Cyan,

        /// <summary><c>sky</c>.</summary>
        Sky,

        /// <summary><c>blue</c>.</summary>
        Blue,

        /// <summary><c>indigo</c>.</summary>
        Indigo,

        /// <summary><c>violet</c>.</summary>
        Violet,

        /// <summary><c>purple</c>.</summary>
        Purple,

        /// <summary><c>fuchsia</c>.</summary>
        Fuchsia,

        /// <summary><c>pink</c>.</summary>
        Pink,

        /// <summary><c>rose</c>.</summary>
        Rose,

        /// <summary><c>slate</c>.</summary>
        Slate,

        /// <summary><c>gray</c>.</summary>
        Gray,

        /// <summary><c>zinc</c>.</summary>
        Zinc,

        /// <summary><c>neutral</c>.</summary>
        Neutral,

        /// <summary><c>stone</c>.</summary>
        Stone,
    }
}
