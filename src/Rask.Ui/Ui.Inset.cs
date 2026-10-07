namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     The sides a component pulls outwards on, so what it shows lines up with its surroundings and not
    ///     with its own invisible padding. Flux's <c>inset</c>.
    /// </summary>
    [Flags]
    public enum Inset
    {
        /// <summary>No side.</summary>
        None = 0,

        /// <summary>The top.</summary>
        Top = 1,

        /// <summary>The bottom.</summary>
        Bottom = 2,

        /// <summary>The reading-start side.</summary>
        Left = 4,

        /// <summary>The reading-end side.</summary>
        Right = 8,

        /// <summary>Every side, which is what Flux's bare <c>inset</c> means.</summary>
        All = Top | Bottom | Left | Right,
    }
}
