using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Rask.Core.Globalization;

/// <summary>
///     Reads the framework's own user-visible text, translated when an app supplies a translation.
/// </summary>
/// <remarks>
///     <para>
///         An app translates these by adding a reserved catalog, <c>Resources/RaskStrings.{culture}.json</c>,
///         whose keys are <see cref="RaskString" /> names. There is no neutral file to write: the English
///         defaults are the literals at each call site, which is what guarantees the framework can never
///         have a missing string.
///     </para>
///     <code>
///     // Resources/RaskStrings.hu.json
///     { "PickerClear": "Törlés", "PaginationSummary": "{0}–{1} megjelenítése, összesen {2}" }
///     </code>
///     <para>
///         A text that carries values numbers them — <c>{0}</c>, <c>{1}</c> — so a translation can put them
///         in its own order, and may give one a .NET format (<c>{2:N0}</c>). The values are written in the
///         visitor's culture.
///     </para>
/// </remarks>
public static class RaskStrings
{
    // The English of each text that carries values, parsed once. Indexed by key, and a benign race: two
    // threads parsing the same literal store equal results.
    private static readonly CompositeFormat?[] _english = new CompositeFormat?[Enum.GetValues<RaskString>().Length];

    // A translation parsed once. Bounded by what the app's catalogs hold.
    private static readonly ConcurrentDictionary<string, CompositeFormat?> _translated = new(StringComparer.Ordinal);

    // Set from a generated [ModuleInitializer] when an app ships a RaskStrings catalog. Null otherwise.
    internal static IRaskStringSource? Source { get; private set; }

    // The translations libraries carry for the texts they render, asked after the app's own. Replaced
    // whole on each registration, so a reader never sees one half written.
    private static IRaskStringSource[] _libraries = [];

    /// <summary>
    ///     Registers the app's translations for the framework's own text. Called by generated code.
    /// </summary>
    /// <remarks>The app's word is final: its translation of a key is used over any library's.</remarks>
    public static void UseSource(IRaskStringSource source) => Source = source;

    /// <summary>
    ///     Registers the translations a library ships for the framework texts it renders — the UI kit's
    ///     Hungarian. Called by generated code.
    /// </summary>
    /// <remarks>
    ///     A library's translation is used where the app has none for that key, and ahead of the English.
    ///     A library opts its <c>Resources/RaskStrings.{culture}.json</c> into this with
    ///     <c>&lt;RaskStringsLibrary&gt;true&lt;/RaskStringsLibrary&gt;</c>.
    /// </remarks>
    public static void UseLibrarySource(IRaskStringSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_translated)
        {
            if (Array.IndexOf(_libraries, source) < 0)
            {
                _libraries = [.. _libraries, source];
            }
        }
    }

    /// <summary>
    ///     The translated text for <paramref name="key" />, or <paramref name="fallback" /> — the
    ///     framework's English — when the app has no translation for it.
    /// </summary>
    /// <remarks>
    ///     Reading this consults the visitor's UI language through <see cref="RaskCulture.CurrentUI" />,
    ///     which also marks the calling component as culture-dependent so a language switch repaints it.
    /// </remarks>
    public static string Get(RaskString key, string fallback) => Translation(key) ?? fallback;

    /// <summary>
    ///     The text for <paramref name="key" /> with <paramref name="arg0" /> written into its <c>{0}</c>.
    /// </summary>
    /// <remarks>
    ///     <paramref name="fallback" /> is the framework's English and is itself such a text:
    ///     <c>RaskStrings.Get(RaskString.RatingValue, "{0} of {1}", star, max)</c>. A translation that asks
    ///     for a value the text does not carry is passed over for the English, so this never throws.
    /// </remarks>
    public static string Get<T0>(RaskString key, string fallback, T0 arg0)
    {
        var (text, culture) = Template(key, fallback, 1);
        return string.Format(culture, text, arg0);
    }

    /// <summary>
    ///     The text for <paramref name="key" /> with two values written into its <c>{0}</c> and <c>{1}</c>.
    /// </summary>
    /// <inheritdoc cref="Get{T0}(RaskString, string, T0)" path="/remarks" />
    public static string Get<T0, T1>(RaskString key, string fallback, T0 arg0, T1 arg1)
    {
        var (text, culture) = Template(key, fallback, 2);
        return string.Format(culture, text, arg0, arg1);
    }

    /// <summary>
    ///     The text for <paramref name="key" /> with three values written into its <c>{0}</c>, <c>{1}</c>
    ///     and <c>{2}</c>.
    /// </summary>
    /// <inheritdoc cref="Get{T0}(RaskString, string, T0)" path="/remarks" />
    public static string Get<T0, T1, T2>(RaskString key, string fallback, T0 arg0, T1 arg1, T2 arg2)
    {
        var (text, culture) = Template(key, fallback, 3);
        return string.Format(culture, text, arg0, arg1, arg2);
    }

    /// <summary>Test-only: forgets every registered source.</summary>
    internal static void ResetForTests()
    {
        Source = null;
        _libraries = [];
    }

    // The app's translation, else the first library's, else nothing. An app with no catalog and no
    // languages of its own — the common case — pays three reads here and never asks for the culture.
    private static string? Translation(RaskString key)
    {
        var app = Source;

        // A library speaks only the languages the app says it ships: without that list the language is
        // the machine's, and an English app would turn Hungarian on a Hungarian server.
        var libraries = RaskCulture.IsEnabled ? _libraries : [];
        if (app is null && libraries.Length == 0)
        {
            return null;
        }

        var tag = RaskCulture.CurrentUI.Name;
        if (app?.Get(key, tag) is { } own)
        {
            return own;
        }

        foreach (var library in libraries)
        {
            if (library.Get(key, tag) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    // The text to write the values into, and the culture to write them in.
    private static (CompositeFormat Text, IFormatProvider? Culture) Template(RaskString key, string fallback, int values)
    {
        if (Translation(key) is { } translation
            && _translated.GetOrAdd(translation, Parse) is { } translated
            && translated.MinimumArgumentCount <= values)
        {
            return (translated, RaskCulture.Current);
        }

        // The English writes its values the way the kit always has: culture-neutral.
        return (_english[(int)key] ??= CompositeFormat.Parse(fallback), CultureInfo.InvariantCulture);
    }

    // Once per translation, so a hand-written source's stray brace costs one exception and not one a render.
    private static CompositeFormat? Parse(string text)
    {
        try
        {
            return CompositeFormat.Parse(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
