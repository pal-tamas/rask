namespace Rask;

public static partial class Ui
{
    /// <summary>How large a calendar's day cells are — Flux UI's <c>size</c>.</summary>
    public enum CalendarSize
    {
        /// <summary>44px cells.</summary>
        Base = 0,

        /// <summary>36px cells.</summary>
        Xs,

        /// <summary>40px cells — what a date picker's popup uses.</summary>
        Sm,

        /// <summary>48px cells.</summary>
        Lg,

        /// <summary>56px cells.</summary>
        Xl,

        /// <summary>64px cells — Flux's <c>2xl</c>.</summary>
        Xxl,
    }
}
