namespace Rask;

public static partial class Ui
{
    /// <summary>How tall a <see cref="UiSelect{T}" /> is: Flux's <c>size</c>.</summary>
    public enum SelectSize
    {
        /// <summary>40px, Flux's default.</summary>
        Base = 0,

        /// <summary>32px.</summary>
        Sm,

        /// <summary>24px, with smaller text.</summary>
        Xs,
    }
}
