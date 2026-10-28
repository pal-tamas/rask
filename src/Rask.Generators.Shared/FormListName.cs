using System;

namespace Rask.Generators.Shared;

/// <summary>
///     The name a form gives the list of ids that stands for a collection of links: <c>Activities</c> →
///     <c>ActivityIds</c>, <c>Vehicles</c> → <c>VehicleIds</c>, <c>Statuses</c> → <c>StatusIds</c>.
/// </summary>
/// <remarks>
///     <para>
///         Rails' rules, the few that matter for a collection's name: a short list of irregular plurals
///         (<c>People</c> → <c>Person</c>, <c>Children</c> → <c>Child</c>), then <c>ies</c> → <c>y</c>, then <c>es</c>
///         dropped where it was added (after x/ch/sh/ss/zz, and <c>Statuses</c>, <c>Buses</c>, <c>Aliases</c> and their
///         kin — never a plain <c>ses</c>, so <c>Houses</c> → <c>House</c>), then a final <c>s</c>. A name none of them
///         makes singular — <c>Staff</c>, or one ending in <c>ss</c> — keeps the collection's own name: <c>StaffIds</c>.
///     </para>
///     <para>
///         Shared as source by the generator that writes the list and by Rask.Data, which names the same field when
///         the database refuses one of its ids.
///     </para>
/// </remarks>
internal static class FormListName
{
    // Plural → singular, as the whole name or its last word, so a prefix rides along: ContactPeople → ContactPerson.
    private static readonly (string Plural, string Singular)[] Irregular =
    [
        ("People", "Person"), ("Children", "Child"), ("Men", "Man"), ("Women", "Woman"),
        ("Mice", "Mouse"), ("Geese", "Goose"), ("Teeth", "Tooth"), ("Feet", "Foot"),
        ("Movies", "Movie"), ("Series", "Series"), ("Species", "Species"),
    ];

    // Where "es" was added rather than "s": after x/ch/sh/ss/zz, and after a word that ends in "us" or "as". A plain "ses"
    // is not one — Houses, Cases and Courses only took an "s" — so those words are named, as Rails names them.
    private static readonly string[] EsEndings =
    [
        "xes", "ches", "shes", "sses", "zzes",
        "statuses", "buses", "aliases", "campuses", "viruses", "bonuses", "censuses", "consensuses", "focuses", "syllabuses",
    ];

    /// <summary>The list of ids for <paramref name="collection" />.</summary>
    public static string Ids(string collection) => Singular(collection) + "Ids";

    /// <summary><paramref name="plural" /> made singular, or itself when no rule applies.</summary>
    public static string Singular(string plural)
    {
        foreach (var (many, one) in Irregular)
        {
            // The whole name or its last word — a property name is PascalCase: ContactPeople, never the "men" of Specimens.
            if (plural.EndsWith(many, StringComparison.Ordinal))
            {
                return plural.Remove(plural.Length - many.Length) + one;
            }
        }

        if (plural.Length > 3 && plural.EndsWith("ies", StringComparison.Ordinal))
        {
            return plural.Remove(plural.Length - 3) + "y";
        }

        foreach (var ending in EsEndings)
        {
            if (plural.Length > 3 && plural.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            {
                return plural.Remove(plural.Length - 2);
            }
        }

        return plural.Length > 1 && plural[plural.Length - 1] == 's' && plural[plural.Length - 2] != 's'
            ? plural.Remove(plural.Length - 1)
            : plural;
    }
}
