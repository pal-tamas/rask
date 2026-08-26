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

/// <summary>
///     Supplies translated text for the framework's own strings. Implemented by the generated catalog.
/// </summary>
public interface IRaskStringSource
{
    /// <summary>
    ///     The text for <paramref name="key" /> in <paramref name="cultureTag" />, or <c>null</c> to use
    ///     the framework's English default.
    /// </summary>
    string? Get(RaskString key, string cultureTag);
}

/// <summary>
///     Reads the framework's own user-visible text, translated when an app supplies a translation.
/// </summary>
/// <remarks>
///     An app translates these by adding a reserved catalog, <c>Resources/RaskStrings.{culture}.json</c>,
///     whose keys are <see cref="RaskString" /> names. There is no neutral file to write: the English
///     defaults are the literals at each call site, which is what guarantees the framework can never
///     have a missing string.
///     <code>
///     // Resources/RaskStrings.hu.json
///     { "PickerClear": "Törlés", "PickerPreviousMonth": "Előző hónap" }
///     </code>
/// </remarks>
public static class RaskStrings
{
    // Set from a generated [ModuleInitializer] when an app ships a RaskStrings catalog. Null otherwise,
    // which is the common case and costs one null check.
    internal static IRaskStringSource? Source { get; private set; }

    /// <summary>
    ///     Registers the app's translations for the framework's own text. Called by generated code.
    /// </summary>
    public static void UseSource(IRaskStringSource source) => Source = source;

    /// <summary>
    ///     The translated text for <paramref name="key" />, or <paramref name="fallback" /> — the
    ///     framework's English — when the app has no translation for it.
    /// </summary>
    /// <remarks>
    ///     Reading this consults the visitor's UI language through <see cref="RaskCulture.CurrentUI" />,
    ///     which also marks the calling component as culture-dependent so a language switch repaints it.
    /// </remarks>
    public static string Get(RaskString key, string fallback) =>
        Source is { } source ? source.Get(key, RaskCulture.CurrentUI.Name) ?? fallback : fallback;

    /// <summary>Test-only: forgets any registered source.</summary>
    internal static void ResetForTests() => Source = null;
}
