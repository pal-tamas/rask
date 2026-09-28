namespace Rask.Core.Globalization;

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
