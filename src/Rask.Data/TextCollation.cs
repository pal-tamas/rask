using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;

namespace Rask.Data;

/// <summary>
///     What a SQL Server collation name says about text — its language, and whether case, accents, kana and width tell
///     two strings apart — read so a database without the name can compare text the same way.
/// </summary>
/// <remarks>
///     <c>Hungarian_100_CI_AS</c> is Hungarian (<c>hu-HU</c>), case-insensitive, accent-sensitive, and — saying neither
///     <c>KS</c> nor <c>WS</c> — kana- and width-insensitive, exactly as SQL Server reads it. A <c>SQL_</c> collation
///     (<c>SQL_Latin1_General_CP1_CI_AS</c>) reads the same way, its code page aside; a binary one compares ordinally.
/// </remarks>
internal sealed class TextCollation
{
    /// <summary>The model annotation that carries the collation's SQL Server name to the database's connections.</summary>
    internal const string Annotation = "Rask:TextCollation";

    private static readonly ConcurrentDictionary<string, TextCollation> Parsed = new(StringComparer.OrdinalIgnoreCase);

    // SQL Server's collation designators, each with the language whose order it is. Latin1_General is no one language's:
    // it orders as the invariant culture does.
    private static readonly FrozenDictionary<string, string> Languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Albanian"] = "sq-AL",
        ["Arabic"] = "ar-SA",
        ["Azeri_Cyrillic"] = "az-Cyrl-AZ",
        ["Azeri_Latin"] = "az-Latn-AZ",
        ["Bosnian_Latin"] = "bs-Latn-BA",
        ["Chinese_PRC"] = "zh-CN",
        ["Chinese_Taiwan_Stroke"] = "zh-TW",
        ["Croatian"] = "hr-HR",
        ["Cyrillic_General"] = "ru-RU",
        ["Czech"] = "cs-CZ",
        ["Danish_Norwegian"] = "da-DK",
        ["Estonian"] = "et-EE",
        ["Finnish_Swedish"] = "fi-FI",
        ["French"] = "fr-FR",
        ["German_PhoneBook"] = "de-DE",
        ["Greek"] = "el-GR",
        ["Hebrew"] = "he-IL",
        ["Hungarian"] = "hu-HU",
        ["Icelandic"] = "is-IS",
        ["Japanese"] = "ja-JP",
        ["Kazakh"] = "kk-KZ",
        ["Korean"] = "ko-KR",
        ["Korean_Wansung"] = "ko-KR",
        ["Latin1_General"] = "",
        ["Latvian"] = "lv-LV",
        ["Lithuanian"] = "lt-LT",
        ["Macedonian_FYROM"] = "mk-MK",
        ["Modern_Spanish"] = "es-ES",
        ["Norwegian"] = "nb-NO",
        ["Persian"] = "fa-IR",
        ["Polish"] = "pl-PL",
        ["Romanian"] = "ro-RO",
        ["Scandinavian"] = "sv-SE",
        ["Serbian_Cyrillic"] = "sr-Cyrl-RS",
        ["Serbian_Latin"] = "sr-Latn-RS",
        ["Slovak"] = "sk-SK",
        ["Slovenian"] = "sl-SI",
        ["Thai"] = "th-TH",
        ["Turkish"] = "tr-TR",
        ["Ukrainian"] = "uk-UA",
        ["Vietnamese"] = "vi-VN",
        ["Welsh"] = "cy-GB",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private TextCollation(string name, CultureInfo culture, CompareOptions options)
    {
        Name = name;
        Culture = culture;
        Options = options;
    }

    /// <summary>The SQL Server name, as the model wrote it.</summary>
    internal string Name { get; }

    /// <summary>The language whose order it is — the invariant culture for <c>Latin1_General</c>.</summary>
    internal CultureInfo Culture { get; }

    /// <summary>What the comparison ignores: <see cref="CompareOptions.Ordinal" /> alone for a binary collation.</summary>
    internal CompareOptions Options { get; }

    /// <summary>
    ///     PostgreSQL's name for the language's ICU collation, which <c>initdb</c> imports on every server built with ICU:
    ///     <c>hu-HU-x-icu</c>, or <c>und-x-icu</c> for the invariant order.
    /// </summary>
    internal string IcuCollation => (Culture.Name.Length == 0 ? "und" : Culture.Name) + "-x-icu";

    /// <summary>
    ///     The ICU locale of a nondeterministic collation that compares as this one does — <c>hu-HU-u-ks-level2</c> for
    ///     <c>Hungarian_100_CI_AS</c> — or null when the comparison tells case and accents apart (or is binary), which a
    ///     deterministic collation already does.
    /// </summary>
    /// <remarks>
    ///     ICU's strength says what counts: level 1 the letters alone, level 2 their accents too; a case level over
    ///     level 1 tells case apart while ignoring accents. Kana and width count only from level 3 on, so below it they
    ///     are ignored, as SQL Server ignores them without <c>KS</c>/<c>WS</c>.
    /// </remarks>
    internal string? NondeterministicIcuLocale
    {
        get
        {
            if (Options == CompareOptions.Ordinal)
            {
                return null;
            }

            var strength = (Options.HasFlag(CompareOptions.IgnoreCase), Options.HasFlag(CompareOptions.IgnoreNonSpace)) switch
            {
                (true, false) => "ks-level2",
                (true, true) => "ks-level1",
                (false, true) => "ks-level1-kc-true",
                _ => null,
            };

            if (strength is null)
            {
                return null;
            }

            var language = Culture.Name.Length == 0 ? "und" : Culture.Name;
            return $"{language}-u-{strength}";
        }
    }

    /// <summary>Reads <paramref name="name" />, once per name.</summary>
    /// <exception cref="InvalidOperationException">The name is not a SQL Server collation this can read.</exception>
    internal static TextCollation Parse(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Parsed.GetOrAdd(name, Read);
    }

    /// <summary>Compares two strings as the collation does.</summary>
    internal int Compare(string x, string y) =>
        Options == CompareOptions.Ordinal ? string.CompareOrdinal(x, y) : Culture.CompareInfo.Compare(x, y, Options);

    private static TextCollation Read(string name)
    {
        var parts = name.Split('_');
        var start = string.Equals(parts[0], "SQL", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var designator = new List<string>();
        var caseSensitive = true;
        var accentSensitive = true;
        var kanaSensitive = false;
        var widthSensitive = false;
        var binary = false;

        for (var i = start; i < parts.Length; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "CI": caseSensitive = false; break;
                case "CS": caseSensitive = true; break;
                case "AI": accentSensitive = false; break;
                case "AS": accentSensitive = true; break;
                case "KS": kanaSensitive = true; break;
                case "WS": widthSensitive = true; break;
                case "BIN" or "BIN2": binary = true; break;

                // The version, the code page, a preference for upper case and the encoding change nothing a comparison
                // here could honour.
                case "SC" or "UTF8" or "VSS" or "PREF":
                    break;
                case var part when IsVersionOrCodePage(part):
                    break;
                default:
                    designator.Add(parts[i]);
                    break;
            }
        }

        if (!Languages.TryGetValue(string.Join('_', designator), out var language))
        {
            throw new InvalidOperationException(
                $"c.OrderTextBy(\"{name}\") names a collation whose language Rask cannot tell, so on this database it "
                + "cannot compare text the way SQL Server would. Name a Windows collation of one of these languages: "
                + string.Join(", ", Languages.Keys.Order(StringComparer.Ordinal)) + " — e.g. \"Hungarian_100_CI_AS\".");
        }

        var options = binary ? CompareOptions.Ordinal : Ignored(caseSensitive, accentSensitive, kanaSensitive, widthSensitive);
        return new TextCollation(name, CultureInfo.GetCultureInfo(language), options);
    }

    private static CompareOptions Ignored(bool caseSensitive, bool accentSensitive, bool kanaSensitive, bool widthSensitive)
    {
        var options = CompareOptions.None;
        if (!caseSensitive)
        {
            options |= CompareOptions.IgnoreCase;
        }

        if (!accentSensitive)
        {
            options |= CompareOptions.IgnoreNonSpace;
        }

        if (!kanaSensitive)
        {
            options |= CompareOptions.IgnoreKanaType;
        }

        if (!widthSensitive)
        {
            options |= CompareOptions.IgnoreWidth;
        }

        return options;
    }

    private static bool IsVersionOrCodePage(string part) =>
        part.All(char.IsAsciiDigit)
        || (part.StartsWith("CP", StringComparison.OrdinalIgnoreCase) && part.Length > 2 && part[2..].All(char.IsAsciiDigit));
}
