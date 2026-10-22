namespace Rask;

public static partial class Ui
{
    /// <summary>What opens a date picker — Flux UI's <c>type</c>.</summary>
    public enum DatePickerType
    {
        /// <summary>A field-shaped button showing the choice.</summary>
        Button = 0,

        /// <summary>A field to type the date into, month, day and year.</summary>
        Input,
    }

    /// <summary>How large the day cells of a date picker's calendar are — Flux UI's <c>size</c>.</summary>
    public enum DatePickerSize
    {
        /// <summary>40px cells.</summary>
        Default = 0,

        /// <summary>36px cells.</summary>
        Sm,

        /// <summary>44px cells.</summary>
        Lg,

        /// <summary>48px cells.</summary>
        Xl,

        /// <summary>56px cells — Flux's <c>2xl</c>.</summary>
        Xxl,
    }

    /// <summary>
    ///     How a typed date is drawn — <c>flux:date-picker.input</c>'s <c>variant</c> and the date picker's
    ///     <c>with-inputs</c>. Custom unless set, the kit's choice: Flux's docs default to native and call custom
    ///     the recommended and future default, and its own pages draw custom.
    /// </summary>
    public enum DatePickerInputVariant
    {
        /// <summary>Month, day and year typed as parts, in the locale's order.</summary>
        Custom = 0,

        /// <summary>The browser's own date field, <c>&lt;input type="date"&gt;</c>.</summary>
        Native,
    }

    /// <summary>How tall a date picker's typed field is — <c>flux:date-picker.input</c>'s <c>size</c>.</summary>
    public enum DatePickerInputSize
    {
        /// <summary>40px.</summary>
        Base = 0,

        /// <summary>32px.</summary>
        Sm,

        /// <summary>24px.</summary>
        Xs,
    }

    /// <summary>How tall a date picker's button is — <c>flux:date-picker.button</c>'s <c>size</c>.</summary>
    public enum DatePickerButtonSize
    {
        /// <summary>40px.</summary>
        Base = 0,

        /// <summary>32px.</summary>
        Sm,

        /// <summary>24px.</summary>
        Xs,
    }
}
