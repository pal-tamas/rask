using System.Text;

namespace Rask.Data;

/// <summary>
///     What <c>rask audit</c> found, gathered as it reads: each stored row a rule refuses, under its aggregate, its field
///     and the rule's words — the ids only, never what the row holds.
/// </summary>
internal sealed class RuleAuditFindings
{
    private readonly Dictionary<(string Aggregate, string Field, string Rule), List<string>> _breaches = [];
    private readonly Dictionary<string, int> _read = new(StringComparer.Ordinal);
    private readonly List<string> _notes = [];

    /// <summary>Records that <paramref name="id" /> of <paramref name="aggregate" /> breaks <paramref name="rule" /> at <paramref name="field" />.</summary>
    /// <param name="aggregate">The aggregate's (or the table's entity's) name.</param>
    /// <param name="field">The field as a form names it; a child's position is folded to <c>[*]</c>, so its rows count together.</param>
    /// <param name="rule">What the rule says.</param>
    /// <param name="id">The aggregate's id, as text.</param>
    internal void Add(string aggregate, string field, string rule, string id)
    {
        var key = (aggregate, AnyPosition(field), rule);
        if (!_breaches.TryGetValue(key, out var ids))
        {
            _breaches[key] = ids = [];
        }

        // An aggregate breaking one rule on several of its rows is one aggregate to look at.
        if (ids.Count == 0 || !string.Equals(ids[^1], id, StringComparison.Ordinal))
        {
            ids.Add(id);
        }
    }

    /// <summary>Records that <paramref name="count" /> more rows of <paramref name="aggregate" /> were asked.</summary>
    internal void Read(string aggregate, int count) => _read[aggregate] = _read.GetValueOrDefault(aggregate) + count;

    /// <summary>Records something the audit could not ask, said in the report beside the findings.</summary>
    internal void Note(string note) => _notes.Add(note);

    /// <summary>The findings, each aggregate's together, its fields in order.</summary>
    internal RuleAuditReport Report() => new(
        [.. _read.OrderBy(static r => r.Key, StringComparer.Ordinal).Select(static r => new RuleAuditCount(r.Key, r.Value))],
        [
            .. _breaches
                .OrderBy(static b => b.Key.Aggregate, StringComparer.Ordinal)
                .ThenBy(static b => b.Key.Field, StringComparer.Ordinal)
                .ThenBy(static b => b.Key.Rule, StringComparer.Ordinal)
                .Select(static b => new RuleBreach(b.Key.Aggregate, b.Key.Field, b.Key.Rule, [.. b.Value.Distinct(StringComparer.Ordinal)])),
        ])
    {
        Notes = [.. _notes],
    };

    // `Lines[2].Taxes[0].Rate` → `Lines[*].Taxes[*].Rate`.
    private static string AnyPosition(string field)
    {
        if (!field.Contains('[', StringComparison.Ordinal))
        {
            return field;
        }

        var folded = new StringBuilder(field.Length);
        var inside = false;
        foreach (var c in field)
        {
            if (inside)
            {
                inside = c != ']';
                if (!inside)
                {
                    folded.Append("*]");
                }

                continue;
            }

            folded.Append(c);
            inside = c == '[';
        }

        return folded.ToString();
    }
}
