using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rask.Generators.Shared;

/// <summary>
///     Rask's own names for what a model stores, PostgreSQL's style on every database: a table is its class in snake case
///     and plural (<c>DeliveryNote</c> → <c>delivery_notes</c>), a column its property in snake case (<c>PistolId</c> →
///     <c>pistol_id</c>), and a constraint its kind's prefix, its table and its columns (<c>ix_fuelings_pistol_id</c>).
/// </summary>
/// <remarks>
///     <para>
///         Shared as source by Rask.Data, which names the model by it, and by the analyzer that reports a name stated in
///         <c>Configure</c> that it would give anyway (RASK192) — so the two can never disagree.
///     </para>
///     <para>
///         The plural is Rails' (and Laravel's): its rules, irregulars and uncountables, applied to the last word only —
///         <c>sales_person</c> → <c>sales_people</c>. EF Core's own pluralizer lives in its design-time package and
///         Humanizer would be a dependency of every app for one method, so the rules are here, a page long.
///     </para>
///     <para>
///         Every name is cut to <see cref="MaxBytes" /> — PostgreSQL's limit, within SQL Server's 128 — the same way on
///         every database, so a model's names do not depend on where it runs: a longer one keeps what fits and ends in
///         <c>_</c> and a hash of the whole name, which tells two long names apart.
///     </para>
/// </remarks>
internal static class StoreNames
{
    /// <summary>The longest name, in UTF-8 bytes: PostgreSQL's 63.</summary>
    public const int MaxBytes = 63;

    /// <summary>A primary key's prefix: <c>pk_fuelings</c>.</summary>
    public const string PrimaryKey = "pk";

    /// <summary>A foreign key's prefix: <c>fk_fuelings_pistol_id</c>.</summary>
    public const string ForeignKey = "fk";

    /// <summary>A unique index's or alternate key's prefix: <c>uq_users_tenant_key_email</c>.</summary>
    public const string Unique = "uq";

    /// <summary>An index's prefix: <c>ix_fuelings_pistol_id</c>.</summary>
    public const string Index = "ix";

    /// <summary>A check constraint's prefix: <c>ck_bookings_ends_after_start</c>.</summary>
    public const string Check = "ck";

    /// <summary>An exclusion constraint's prefix: <c>xc_bookings_room_id_starts_at_ends_at</c>.</summary>
    public const string Exclusion = "xc";

    /// <summary>A column default's prefix, a named constraint on SQL Server only: <c>df_jobs_awaited</c>.</summary>
    public const string Default = "df";

    private const int HashLength = 8;

    // Words whose plural is themselves.
    private static readonly HashSet<string> Uncountable = new(StringComparer.Ordinal)
    {
        "equipment", "information", "rice", "money", "species", "series", "fish", "sheep", "jeans", "police",
    };

    // Singular → plural, as the whole last word; a word already in its plural keeps it.
    private static readonly Dictionary<string, string> Irregular = new(StringComparer.Ordinal)
    {
        ["person"] = "people",
        ["man"] = "men",
        ["woman"] = "women",
        ["child"] = "children",
        ["sex"] = "sexes",
        ["move"] = "moves",
        ["zombie"] = "zombies",
        ["foot"] = "feet",
        ["tooth"] = "teeth",
        ["goose"] = "geese",
    };

    /// <summary>A table's name for the class <paramref name="className" />: <c>Company</c> → <c>companies</c>.</summary>
    public static string Table(string className) => Fit(Plural(SnakeCase(className)));

    /// <summary>
    ///     A column's name for a property and the value objects and owned types around it, each step in snake case:
    ///     <c>["Km", "Start"]</c> → <c>km_start</c>.
    /// </summary>
    public static string Column(IEnumerable<string> path)
    {
        var name = new StringBuilder();
        foreach (var step in path)
        {
            name.Append(name.Length == 0 ? string.Empty : "_").Append(SnakeCase(step));
        }

        return Fit(name.ToString());
    }

    /// <summary>A constraint's name: its prefix, its table and its columns, <c>ix_fuelings_pistol_id</c>.</summary>
    public static string Constraint(string prefix, string table, IEnumerable<string> columns)
    {
        var name = new StringBuilder(prefix).Append('_').Append(table);
        foreach (var column in columns)
        {
            name.Append('_').Append(column);
        }

        return Fit(name.ToString());
    }

    /// <summary>
    ///     <paramref name="name" /> in snake case, as <c>JsonNamingPolicy.SnakeCaseLower</c> writes it: <c>VATNumber</c> →
    ///     <c>vat_number</c>, <c>Sha256</c> → <c>sha256</c>.
    /// </summary>
    public static string SnakeCase(string name)
    {
        var snake = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                // A word starts at a capital after a small letter or a digit, and at the last capital of an acronym.
                var previous = i > 0 ? name[i - 1] : '\0';
                var startsWord = i > 0 && (char.IsLower(previous) || char.IsDigit(previous) ||
                                           (char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (startsWord && snake.Length > 0 && snake[snake.Length - 1] != '_')
                {
                    snake.Append('_');
                }

                snake.Append(char.ToLowerInvariant(c));
            }
            else
            {
                snake.Append(c);
            }
        }

        return snake.ToString();
    }

    /// <summary>The plural of a snake-case name, by its last word: <c>delivery_note</c> → <c>delivery_notes</c>.</summary>
    public static string Plural(string snake)
    {
        var at = snake.LastIndexOf('_') + 1;
        return Swap(snake, snake.Length - at, PluralWord(snake.Substring(at)));
    }

    /// <summary>
    ///     <paramref name="name" /> when it fits in <see cref="MaxBytes" />; otherwise what fits of it, then <c>_</c> and the
    ///     whole name's hash.
    /// </summary>
    public static string Fit(string name) => Fit(name, MaxBytes);

    /// <summary>
    ///     <paramref name="name" /> when it fits in <paramref name="maxBytes" />; otherwise what fits of it, then <c>_</c>
    ///     and the whole name's hash.
    /// </summary>
    public static string Fit(string name, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(name) <= maxBytes)
        {
            return name;
        }

        var room = Math.Max(0, maxBytes - HashLength - 1);
        var kept = name.Length;
        while (Encoding.UTF8.GetByteCount(name.ToCharArray(0, kept)) > room || (kept > 0 && char.IsHighSurrogate(name[kept - 1])))
        {
            kept--;
        }

        return Swap(name, name.Length - kept, string.Empty).TrimEnd('_') + "_" + Hash(name);
    }

    private static string PluralWord(string word)
    {
        if (word.Length == 0 || Uncountable.Contains(word) || ContainsValue(Irregular, word))
        {
            return word;
        }

        if (Irregular.TryGetValue(word, out var irregular))
        {
            return irregular;
        }

        return Rule(word);
    }

    // Rails' rules, the last it defines first: the first that matches makes the plural.
    private static string Rule(string word) => word switch
    {
        _ when word.EndsWith("quiz", StringComparison.Ordinal) => word + "zes",
        "ox" => "oxen",
        "oxen" or "mice" or "lice" => word,
        "mouse" or "louse" => Swap(word, 4, "ice"),
        _ when EndsWithAny(word, "matrix", "vertex", "index") => Swap(word, 2, "ices"),
        _ when EndsWithAny(word, "x", "ch", "ss", "sh") => word + "es",
        _ when word.EndsWith("quy", StringComparison.Ordinal) || (word[word.Length - 1] == 'y' && word.Length > 1 && !IsVowelOrY(word[word.Length - 2])) =>
            Swap(word, 1, "ies"),
        _ when word.EndsWith("hive", StringComparison.Ordinal) => word + "s",
        _ when word.Length > 2 && word.EndsWith("fe", StringComparison.Ordinal) && word[word.Length - 3] != 'f' => Swap(word, 2, "ves"),
        _ when EndsWithAny(word, "lf", "rf") => Swap(word, 1, "ves"),
        _ when word.EndsWith("sis", StringComparison.Ordinal) => Swap(word, 3, "ses"),
        _ when EndsWithAny(word, "ta", "ia") => word,
        _ when EndsWithAny(word, "tum", "ium") => Swap(word, 2, "a"),
        _ when EndsWithAny(word, "buffalo", "tomato") => word + "es",
        _ when word.EndsWith("bus", StringComparison.Ordinal) => word + "es",
        _ when EndsWithAny(word, "alias", "status") => word + "es",
        _ when EndsWithAny(word, "octopi", "viri") => word,
        _ when EndsWithAny(word, "octopus", "virus") => Swap(word, 2, "i"),
        "axis" or "testis" => Swap(word, 2, "es"),
        _ when word[word.Length - 1] == 's' => word,
        _ => word + "s",
    };

    // The word with its last `drop` letters swapped for `ending`.
    private static string Swap(string word, int drop, string ending) =>
        new StringBuilder(word, 0, word.Length - drop, word.Length - drop + ending.Length).Append(ending).ToString();

    private static bool IsVowelOrY(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static bool EndsWithAny(string word, params string[] endings) =>
        endings.Any(ending => word.EndsWith(ending, StringComparison.Ordinal));

    private static bool ContainsValue(Dictionary<string, string> irregular, string word) =>
        irregular.Values.Any(plural => string.Equals(plural, word, StringComparison.Ordinal));

    // FNV-1a over the name's UTF-8 bytes: the same on every machine and every run, unlike string.GetHashCode.
    private static string Hash(string name)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            hash = unchecked((hash ^ b) * 16777619u);
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
