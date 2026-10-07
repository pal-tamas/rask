namespace Rask;

public static partial class Ui
{
    /// <summary>How tall a <see cref="UiInput{T}" /> is, and an input group's prefix or suffix beside it.</summary>
    public enum InputSize
    {
        /// <summary>40px, Flux's default.</summary>
        Base = 0,

        /// <summary>32px.</summary>
        Sm,

        /// <summary>24px, with smaller text.</summary>
        Xs,
    }
}
