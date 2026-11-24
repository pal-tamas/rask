namespace Rask;

/// <summary>What <c>rask audit</c> was asked for: the aggregates to read (none named: every one), and whether as JSON.</summary>
/// <param name="Aggregates">The aggregates named with <c>--aggregate</c>.</param>
/// <param name="Json">Whether the report is written as JSON.</param>
internal sealed record AuditOptions(IReadOnlyList<string> Aggregates, bool Json)
{
    /// <summary>The options, or null for a command line that says anything else.</summary>
    /// <param name="args">What followed <c>rask audit</c>.</param>
    /// <returns>The options; null when one is unknown or <c>--aggregate</c> names nothing.</returns>
    internal static AuditOptions? Parse(IReadOnlyList<string> args)
    {
        var aggregates = new List<string>();
        var json = false;
        var named = false;
        foreach (var arg in args)
        {
            if (named)
            {
                // One name, or several split by commas: --aggregate Driver,Vehicle.
                aggregates.AddRange(arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                named = false;
                continue;
            }

            switch (arg)
            {
                case "--json":
                    json = true;
                    break;
                case "--aggregate" or "-a":
                    named = true;
                    break;
                default:
                    return null;
            }
        }

        return named ? null : new AuditOptions(aggregates, json);
    }
}
