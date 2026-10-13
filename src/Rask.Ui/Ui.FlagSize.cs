namespace Rask;

public static partial class Ui
{
    /// <summary>How wide a <see cref="UiFlag" /> is. Its height follows: 3:2, or 1:1 for a circle.</summary>
    public enum FlagSize
    {
        /// <summary>24px. Flux's default.</summary>
        Sm,

        /// <summary>20px.</summary>
        Xs,

        /// <summary>32px.</summary>
        Md,

        /// <summary>40px.</summary>
        Lg,

        /// <summary>48px.</summary>
        Xl,
    }
}
