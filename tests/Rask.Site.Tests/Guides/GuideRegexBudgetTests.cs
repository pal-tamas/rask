using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Site.Features;

namespace Rask.Site.Tests.Guides;

// The guides are the repository's own text, rendered at build time and in the reader's browser. A match timeout
// on a pattern over them guards nothing, and it is a budget for the whole Replace — the evaluator's work
// included — so a second covered highlighting every sample of a guide and a starved core failed the render.
public sealed class GuideRegexBudgetTests
{
    [Theory]
    [InlineData(typeof(Markdown))]
    [InlineData(typeof(LlmsText))]
    public void No_pattern_over_the_guides_is_bound_by_a_wall_clock(Type owner)
    {
        var patterns = owner
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.ReturnType == typeof(Regex) && m.GetParameters().Length == 0)
            .ToArray();

        var budgets = patterns.ToDictionary(m => m.Name, m => ((Regex)m.Invoke(null, null)!).MatchTimeout);

        Assert.NotEmpty(budgets);
        Assert.All(budgets, budget => Assert.Equal(Regex.InfiniteMatchTimeout, budget.Value));
    }
}
