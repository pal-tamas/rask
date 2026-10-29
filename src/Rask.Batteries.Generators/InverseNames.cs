using System;
using System.Collections.Generic;
using System.Linq;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
///     Settles which collection of the rows that point at a face keeps a name several navigations want, and what the
///     others are called instead.
/// </summary>
/// <remarks>
///     <para>
///         The first navigation in source order keeps it. Each other one takes the referencing type's plural and its
///         own words: the words of its navigation before those it shares with the target's name, or, when that leaves
///         nothing, its id without <c>Id</c>. <c>ReportColumnColumnId</c> and <c>TotalColumnId</c>, both to a <c>CatalogColumn</c>, give
///         <c>ReportColumnTotals</c> and <c>ReportColumnTotalsTotal</c>.
///     </para>
///     <para>
///         Only a clash between navigations is settled here: a name the target's own members hold is RASK108, as is
///         one whose own words are taken too, or that cannot tell it from another — two <c>FuelingId</c>s.
///     </para>
/// </remarks>
internal sealed class InverseNames
{
    private readonly Dictionary<(string Target, string Name), List<Proposal>> _wanting = new();
    private readonly HashSet<(string Target, string Name)> _given = [];

    /// <summary>Groups <paramref name="proposed" />, in source order, by the name each wants on its target.</summary>
    public InverseNames(IReadOnlyList<Proposal> proposed)
    {
        foreach (var proposal in proposed)
        {
            if (!_wanting.TryGetValue(Key(proposal), out var wanting))
            {
                _wanting[Key(proposal)] = wanting = [];
            }

            wanting.Add(proposal);
        }
    }

    /// <summary>
    ///     Whether <paramref name="proposal" /> keeps the name it wants: no other wants it, or it is the first in source
    ///     order to and the others' own words tell them apart.
    /// </summary>
    public bool Keeps(Proposal proposal)
    {
        var wanting = _wanting[Key(proposal)];
        return ReferenceEquals(wanting[0], proposal) && Separable(wanting);
    }

    /// <summary>Another navigation that wants the name <paramref name="proposal" /> wanted: the one keeping it, if any.</summary>
    public Proposal RivalOf(Proposal proposal)
    {
        var wanting = _wanting[Key(proposal)];
        return ReferenceEquals(wanting[0], proposal) ? wanting[1] : wanting[0];
    }

    /// <summary>
    ///     The name made of <paramref name="proposal" />'s own words, or <c>null</c> when its words add nothing, tell it
    ///     from no rival, or another collection wants or already took that name.
    /// </summary>
    public string? Distinct(Proposal proposal)
    {
        var key = (proposal.Target.FullyQualifiedName, proposal.Distinct);

        return string.Equals(proposal.Distinct, proposal.Name, StringComparison.Ordinal) ||
               !Separable(_wanting[Key(proposal)]) || _wanting.ContainsKey(key) || !_given.Add(key)
            ? null
            : proposal.Distinct;
    }

    /// <summary>
    ///     The words a navigation adds of its own: <c>TotalColumn</c> to a <c>CatalogColumn</c> is <c>Total</c>; one
    ///     that shares every word with its target (<c>Provider</c>, from <c>TransactionTypeId</c>) is its id's,
    ///     <c>TransactionType</c>.
    /// </summary>
    public static string OwnWords(string navigation, string foreignKey, string target)
    {
        var words = Words(navigation);
        var targetWords = Words(target);
        var shared = 0;

        while (shared < words.Count && shared < targetWords.Count &&
               string.Equals(words[words.Count - 1 - shared], targetWords[targetWords.Count - 1 - shared], StringComparison.Ordinal))
        {
            shared++;
        }

        if (shared < words.Count)
        {
            return string.Concat(words.Take(words.Count - shared));
        }

        return foreignKey.Length > 2 && foreignKey.EndsWith("Id", StringComparison.Ordinal)
            ? foreignKey.Substring(0, foreignKey.Length - 2)
            : foreignKey;
    }

    // ReportColumnColumn -> Report, Column, Column: a word starts at each capital.
    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var start = 0;

        for (var i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                words.Add(name.Substring(start, i - start));
                start = i;
            }
        }

        words.Add(name.Substring(start));
        return words;
    }

    // Whether the navigations wanting one name have words of their own that differ: two FuelingIds from two
    // Distribution types do not, and neither keeps the name over the other.
    private static bool Separable(List<Proposal> wanting) =>
        wanting.Select(static p => p.Distinct).Distinct(StringComparer.Ordinal).Count() == wanting.Count;

    private static (string Target, string Name) Key(Proposal proposal) => (proposal.Target.FullyQualifiedName, proposal.Name);

    /// <summary>One navigation's claim to a collection on its target's face.</summary>
    /// <param name="Source">The face holding the navigation.</param>
    /// <param name="ForeignKey">The id the navigation follows.</param>
    /// <param name="Target">The face that gets the collection.</param>
    /// <param name="Name">The name the convention gives it.</param>
    /// <param name="Distinct">The name made of its own words, for when another keeps <paramref name="Name" />.</param>
    internal sealed record Proposal(ReadShape Source, string ForeignKey, ReadShape Target, string Name, string Distinct);
}
