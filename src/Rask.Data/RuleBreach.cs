namespace Rask.Data;

/// <summary>One rule broken by stored rows: where, what it says, and whose they are.</summary>
/// <param name="Aggregate">The aggregate's name, or the entity's for an id rule.</param>
/// <param name="Field">The field as a form names it (a child's position as <c>[*]</c>); empty for the aggregate as a whole.</param>
/// <param name="Rule">What the rule says.</param>
/// <param name="Ids">The ids of the aggregates that break it — never what they hold.</param>
internal sealed record RuleBreach(string Aggregate, string Field, string Rule, IReadOnlyList<string> Ids);
