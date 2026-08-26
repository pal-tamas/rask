using System;
using System.Collections.Generic;

namespace Rask.Generators.Translations;

/// <summary>
///     The CLDR plural categories a language distinguishes, and the arithmetic that picks one.
/// </summary>
/// <remarks>
///     <para>
///         Not ICU MessageFormat. That is unavailable under <c>InvariantGlobalization</c> — the mode a
///         Rask WASM app ships in by default — and would be a large dependency in both an analyzer and
///         the runtime. But MessageFormat is not what is needed here: the plural <em>category
///         function</em> is pure integer arithmetic over the CLDR operands, and it is small enough to
///         carry directly.
///     </para>
///     <para>
///         A curated table, deliberately, rather than a generated dump of CLDR. A language whose grammar
///         Rask does not carry is a build <b>error</b> naming that language — which is honest — instead
///         of silently applying English rules and producing text that reads as broken to every native
///         speaker while every test stays green.
///     </para>
/// </remarks>
internal static class PluralRules
{
    // CLDR category names, in the order a catalog should list them.
    public static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other"];

    private sealed class Rule(string[] categories, string body)
    {
        // Which categories this language actually uses. A catalog naming one outside this set is
        // writing text the language can never select.
        public string[] Categories { get; } = categories;

        // The C# body of `static int Pick(long n)`, returning an index into Categories.
        public string Body { get; } = body;
    }

    // Languages are keyed by their primary subtag: hu-HU and hu share grammar.
    //
    // Sources are the CLDR plural rules; the arithmetic is transcribed rather than derived, and the
    // boundary values that distinguish each branch are asserted in PluralRulesTests.
    private static readonly Dictionary<string, Rule> _rules = new(StringComparer.OrdinalIgnoreCase)
    {
        // one / other — n == 1. The large majority of European languages.
        ["en"] = OneOther,
        ["de"] = OneOther,
        ["nl"] = OneOther,
        ["sv"] = OneOther,
        ["da"] = OneOther,
        ["no"] = OneOther,
        ["nb"] = OneOther,
        ["nn"] = OneOther,
        ["fi"] = OneOther,
        ["et"] = OneOther,
        ["el"] = OneOther,
        ["it"] = OneOther,
        ["es"] = OneOther,
        ["ca"] = OneOther,
        ["hu"] = OneOther,
        ["tr"] = OneOther,
        ["bg"] = OneOther,
        ["he"] = OneOther,
        ["eu"] = OneOther,
        ["af"] = OneOther,
        ["sq"] = OneOther,
        ["ka"] = OneOther,
        ["az"] = OneOther,
        ["kk"] = OneOther,

        // one / other — but 0 counts as "one": French and Brazilian Portuguese.
        ["fr"] = ZeroIsOne,
        ["pt"] = ZeroIsOne,
        ["hy"] = ZeroIsOne,

        // other only — no grammatical number.
        ["ja"] = OtherOnly,
        ["zh"] = OtherOnly,
        ["ko"] = OtherOnly,
        ["vi"] = OtherOnly,
        ["th"] = OtherOnly,
        ["id"] = OtherOnly,
        ["ms"] = OtherOnly,
        ["my"] = OtherOnly,

        ["ru"] = EastSlavic,
        ["uk"] = EastSlavic,
        ["be"] = EastSlavic,
        ["pl"] = Polish,
        ["cs"] = WestSlavic,
        ["sk"] = WestSlavic,
        ["lt"] = Lithuanian,
        ["lv"] = Latvian,
        ["ro"] = Romanian,
        ["ar"] = Arabic,
    };

    private static Rule OneOther => new(
        ["one", "other"],
        "return n == 1 ? 0 : 1;");

    // i == 0 || i == 1 — French treats zero as singular ("0 jour").
    private static Rule ZeroIsOne => new(
        ["one", "other"],
        "return (n == 0 || n == 1) ? 0 : 1;");

    private static Rule OtherOnly => new(
        ["other"],
        "return 0;");

    // Russian/Ukrainian/Belarusian: one (n%10==1 && n%100!=11), few (n%10 in 2..4 && n%100 not in
    // 12..14), many (everything else).
    private static Rule EastSlavic => new(
        ["one", "few", "many"],
        """
        var m10 = n % 10;
                var m100 = n % 100;
                if (m10 == 1 && m100 != 11) return 0;
                if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return 1;
                return 2;
        """);

    // Polish: one (n==1), few (n%10 in 2..4 && n%100 not in 12..14), many (the rest).
    private static Rule Polish => new(
        ["one", "few", "many"],
        """
        if (n == 1) return 0;
                var m10 = n % 10;
                var m100 = n % 100;
                if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return 1;
                return 2;
        """);

    // Czech/Slovak: one (1), few (2..4), other (the rest). CLDR also has "many" for fractions, which
    // an integer count cannot reach.
    private static Rule WestSlavic => new(
        ["one", "few", "other"],
        """
        if (n == 1) return 0;
                if (n >= 2 && n <= 4) return 1;
                return 2;
        """);

    // Lithuanian: one (n%10==1 && n%100 not in 11..19), few (n%10 in 2..9 && n%100 not in 11..19),
    // other.
    private static Rule Lithuanian => new(
        ["one", "few", "other"],
        """
        var m10 = n % 10;
                var m100 = n % 100;
                if (m10 == 1 && (m100 < 11 || m100 > 19)) return 0;
                if (m10 >= 2 && m10 <= 9 && (m100 < 11 || m100 > 19)) return 1;
                return 2;
        """);

    // Latvian: zero (n%10==0 or n%100 in 11..19), one (n%10==1 && n%100!=11), other.
    private static Rule Latvian => new(
        ["zero", "one", "other"],
        """
        var m10 = n % 10;
                var m100 = n % 100;
                if (m10 == 0 || (m100 >= 11 && m100 <= 19)) return 0;
                if (m10 == 1 && m100 != 11) return 1;
                return 2;
        """);

    // Romanian: one (1), few (0, or n%100 in 1..19), other.
    private static Rule Romanian => new(
        ["one", "few", "other"],
        """
        if (n == 1) return 0;
                var m100 = n % 100;
                if (n == 0 || (m100 >= 1 && m100 <= 19)) return 1;
                return 2;
        """);

    // Arabic: all six.
    private static Rule Arabic => new(
        ["zero", "one", "two", "few", "many", "other"],
        """
        if (n == 0) return 0;
                if (n == 1) return 1;
                if (n == 2) return 2;
                var m100 = n % 100;
                if (m100 >= 3 && m100 <= 10) return 3;
                if (m100 >= 11 && m100 <= 99) return 4;
                return 5;
        """);

    /// <summary>The categories a language distinguishes, or <c>null</c> when Rask does not carry it.</summary>
    /// <remarks>
    ///     These are the categories an integer count can actually select. CLDR also defines forms that
    ///     only fractions reach — Czech's "many" is 1.5 rather than any whole number — and listing those
    ///     would ask an author to write text no visitor could ever see.
    /// </remarks>
    public static string[]? CategoriesFor(string cultureTag) =>
        _rules.TryGetValue(PrimaryLanguage(cultureTag), out var rule) ? rule.Categories : null;

    /// <summary>
    ///     The category a language falls back to — the arm every count that matched nothing else lands on.
    /// </summary>
    /// <remarks>
    ///     <b>Not always "other".</b> Polish integers never select "other": CLDR routes the residual to
    ///     "many", so demanding an "other" form there would force an author to write dead text while
    ///     leaving the form the language actually uses optional. The residual is the last category in
    ///     the language's own list, which is "other" for most languages and "many" for Polish and the
    ///     East Slavic family.
    /// </remarks>
    public static string? ResidualFor(string cultureTag) =>
        _rules.TryGetValue(PrimaryLanguage(cultureTag), out var rule)
            ? rule.Categories[rule.Categories.Length - 1]
            : null;

    /// <summary>The body of the picker for a language, or <c>null</c> when Rask does not carry it.</summary>
    public static string? BodyFor(string cultureTag) =>
        _rules.TryGetValue(PrimaryLanguage(cultureTag), out var rule) ? rule.Body : null;

    /// <summary>Whether <paramref name="name" /> is a CLDR plural category at all.</summary>
    public static bool IsCategory(string name) => Array.IndexOf(Categories, name) >= 0;

    private static string PrimaryLanguage(string tag)
    {
        var cut = tag.IndexOf('-');
        return cut < 0 ? tag : tag.Substring(0, cut);
    }
}
