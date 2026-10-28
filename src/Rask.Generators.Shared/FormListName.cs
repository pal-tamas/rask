using System;

namespace Rask.Generators.Shared;

/// <summary>
///     The name a form gives the list of ids that stands for a collection of links: <c>Activities</c> →
///     <c>ActivityIds</c>, <c>Vehicles</c> → <c>VehicleIds</c>.
/// </summary>
/// <remarks>
///     <para>
///         The documented plural read backwards — <c>ies</c> to <c>y</c>, <c>es</c> dropped after ss/x/z/ch/sh, then a
///         final <c>s</c> — and deliberately not clever, for the reason the plural is not: a name you can guess beats
///         one that is right more often. A collection whose name has no plural ending keeps it: <c>StaffIds</c>.
///     </para>
///     <para>
///         Shared as source by the generator that writes the list and by Rask.Data, which names the same field when
///         the database refuses one of its ids.
///     </para>
/// </remarks>
internal static class FormListName
{
    /// <summary>The list of ids for <paramref name="collection" />.</summary>
    public static string Ids(string collection) => Singular(collection) + "Ids";

    /// <summary><paramref name="plural" /> with its plural ending taken off.</summary>
    public static string Singular(string plural)
    {
        if (plural.Length > 3 && plural.EndsWith("ies", StringComparison.Ordinal))
        {
            return plural.Remove(plural.Length - 3) + "y";
        }

        if (plural.EndsWith("sses", StringComparison.Ordinal) || plural.EndsWith("xes", StringComparison.Ordinal) ||
            plural.EndsWith("zes", StringComparison.Ordinal) || plural.EndsWith("ches", StringComparison.Ordinal) ||
            plural.EndsWith("shes", StringComparison.Ordinal))
        {
            return plural.Remove(plural.Length - 2);
        }

        return plural.Length > 1 && plural[plural.Length - 1] == 's' && plural[plural.Length - 2] != 's'
            ? plural.Remove(plural.Length - 1)
            : plural;
    }
}
