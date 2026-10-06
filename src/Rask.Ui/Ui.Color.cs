namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     A Tailwind hue, for the components Flux lets you colour: <c>Ui.Button.Primary.Blue</c>.
    /// </summary>
    /// <remarks>
    ///     The seventeen chromatic hues in Tailwind's own order, then its five grays. A component decides
    ///     which shades of the hue it paints with; a gray usually means "the default look".
    /// </remarks>
    public enum Color
    {
        /// <summary>Tailwind's <c>red</c>.</summary>
        Red,

        /// <summary>Tailwind's <c>orange</c>.</summary>
        Orange,

        /// <summary>Tailwind's <c>amber</c>.</summary>
        Amber,

        /// <summary>Tailwind's <c>yellow</c>.</summary>
        Yellow,

        /// <summary>Tailwind's <c>lime</c>.</summary>
        Lime,

        /// <summary>Tailwind's <c>green</c>.</summary>
        Green,

        /// <summary>Tailwind's <c>emerald</c>.</summary>
        Emerald,

        /// <summary>Tailwind's <c>teal</c>.</summary>
        Teal,

        /// <summary>Tailwind's <c>cyan</c>.</summary>
        Cyan,

        /// <summary>Tailwind's <c>sky</c>.</summary>
        Sky,

        /// <summary>Tailwind's <c>blue</c>.</summary>
        Blue,

        /// <summary>Tailwind's <c>indigo</c>.</summary>
        Indigo,

        /// <summary>Tailwind's <c>violet</c>.</summary>
        Violet,

        /// <summary>Tailwind's <c>purple</c>.</summary>
        Purple,

        /// <summary>Tailwind's <c>fuchsia</c>.</summary>
        Fuchsia,

        /// <summary>Tailwind's <c>pink</c>.</summary>
        Pink,

        /// <summary>Tailwind's <c>rose</c>.</summary>
        Rose,

        /// <summary>Tailwind's <c>slate</c>, a gray.</summary>
        Slate,

        /// <summary>Tailwind's <c>gray</c>.</summary>
        Gray,

        /// <summary>Tailwind's <c>zinc</c>, the gray Flux is drawn in.</summary>
        Zinc,

        /// <summary>Tailwind's <c>neutral</c>, a gray.</summary>
        Neutral,

        /// <summary>Tailwind's <c>stone</c>, a gray.</summary>
        Stone,
    }
}
