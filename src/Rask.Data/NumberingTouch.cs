using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Rask.Data;

/// <summary>
///     What one save touched of one numbering: each group, from the earliest place in its order the save touched (none for
///     the whole of it), the rows that take another's number, and the rows that left every group.
/// </summary>
internal sealed class NumberingTouch
{
    private readonly Dictionary<object, Since> _groups = [];

    /// <summary>The groups, each with where its numbering starts again.</summary>
    internal IReadOnlyDictionary<object, Since> Groups => _groups;

    /// <summary>The rows whose number is another's, as the tracker holds them.</summary>
    internal List<EntityEntry> Parts { get; } = [];

    /// <summary>The rows in no group any more, whose number goes.</summary>
    internal List<EntityEntry> Ungrouped { get; } = [];

    internal bool IsEmpty => _groups.Count == 0 && Parts.Count == 0 && Ungrouped.Count == 0;

    /// <summary>Notes <paramref name="group" /> from <paramref name="first" />, the first member of the order at a touched place.</summary>
    internal void Touch(object group, object? first, bool comparable)
    {
        var since = comparable && first is not null ? new Since(first) : Since.Whole;
        _groups[group] = _groups.TryGetValue(group, out var earlier) ? Since.Earlier(earlier, since) : since;
    }

    /// <summary>The lock names of the groups, under <paramref name="numbering" />.</summary>
    internal IReadOnlyCollection<string> LockNames(string numbering) =>
        [.. _groups.Keys.Select(group => NumberingLocks.Name(numbering, Convert.ToString(group, CultureInfo.InvariantCulture) ?? ""))];

    /// <summary>Where a group's numbering starts again: a value of the order's first member, or the whole of it.</summary>
    internal readonly record struct Since(object? From)
    {
        internal static Since Whole => default;

        internal bool IsWhole => From is null;

        internal static Since Earlier(Since one, Since other)
        {
            if (one.IsWhole || other.IsWhole)
            {
                return Whole;
            }

            return Comparer<object>.Default.Compare(one.From, other.From) <= 0 ? one : other;
        }
    }
}
