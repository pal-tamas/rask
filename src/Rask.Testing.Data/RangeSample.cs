using Rask.Data;

namespace Rask.Testing;

/// <summary>The ends a <see cref="Rask.Data.Range" /> accepts: from its least (or the type's first sample), one step on, within its greatest.</summary>
internal static class RangeSample
{
    public static object?[] For(string name, Type ends, RangeRules rules)
    {
        var start = rules.Min ?? SampleSteps.First(ends)
            ?? throw new InvalidOperationException(
                $"Test.Make<{name}>() builds a range of numbers, dates or times, not of {ends.Name}s. "
                + $"Make this one by hand, with {name}.Create(…).");

        var end = SampleSteps.Step(start);
        if (rules.Max is { } most && end.CompareTo(most) > 0)
        {
            end = most;
            start = rules.Strict ? SampleSteps.Step(most, -1) : most;
        }

        return [start, end];
    }
}
