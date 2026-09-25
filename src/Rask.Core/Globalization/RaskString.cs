namespace Rask.Core.Globalization;

/// <summary>
///     Every user-visible string the framework itself renders.
/// </summary>
/// <remarks>
///     <para>
///         A closed enum rather than string keys, because the framework's own text is a <em>closed</em>
///         set: the lookup is an <c>int</c> switch with no hashing, an unknown key cannot be written,
///         and — the part that matters most in practice — this file is the single enumerable answer to
///         "what English does Rask put on a page?", which nobody could produce before.
///     </para>
///     <para>
///         The enum lives in Core and names Bootstrap concepts (<see cref="PickerClear" />). That is a
///         small deliberate leak: Rask ships as one product, Bootstrap already depends on Core, and the
///         alternative — a key registry per library — trades a closed compile-checked set for
///         stringly-typed keys in three places instead of one.
///     </para>
/// </remarks>
public enum RaskString
{
    /// <summary>The previous-month control's accessible name in a date picker.</summary>
    PickerPreviousMonth,

    /// <summary>The next-month control's accessible name in a date picker.</summary>
    PickerNextMonth,

    /// <summary>The hour column's label in a time picker.</summary>
    PickerHour,

    /// <summary>The minute column's label in a time picker.</summary>
    PickerMinute,

    /// <summary>The seconds column's label in a time picker.</summary>
    PickerSecond,

    /// <summary>The clear (×) control's accessible name in a picker.</summary>
    PickerClear,

    /// <summary>The heading of the built-in not-found page.</summary>
    NotFoundTitle,

    /// <summary>The body of the built-in not-found page.</summary>
    NotFoundBody,

    /// <summary>The link back to the home page on the built-in not-found page.</summary>
    NotFoundBackHome,

    /// <summary>The heading of the built-in error page.</summary>
    ErrorHeading,

    /// <summary>The retry control on the built-in error page.</summary>
    ErrorTryAgain,

    /// <summary>The reload control on the built-in error page.</summary>
    ErrorReload,
}
