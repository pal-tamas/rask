using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The rules of the runtime's command palette (<c>rask-command.ts</c>), run under Node: which commands a query
///     leaves in the list, and which one an arrow key lights.
/// </summary>
public class CommandPaletteTests
{
    [Fact]
    public void A_query_keeps_the_commands_that_contain_it_whatever_the_case_or_the_accents()
    {
        if (NodeFixture.Run("CommandPaletteFixture") is not { } rules)
        {
            return;
        }

        var kept = new[] { "plain", "anyCase", "accentInTheCommand", "accentInTheQuery", "nothingTyped" }.Select(rule => Says(rules, rule));

        Assert.All(kept, Assert.True);
        Assert.False(Says(rules, "noMatch"));
    }

    [Fact]
    public void An_arrow_lights_the_next_command_that_can_be_picked_and_goes_around_the_ends()
    {
        if (NodeFixture.Run("CommandPaletteFixture") is not { } rules)
        {
            return;
        }

        var (next, back) = (Lit(rules, "next"), Lit(rules, "back"));
        var (forward, around) = (Lit(rules, "wrapsForward"), Lit(rules, "wrapsBack"));

        Assert.Equal(2, next);
        Assert.Equal(0, back);
        Assert.Equal(0, forward);
        Assert.Equal(3, around);
    }

    [Fact]
    public void The_highlight_starts_at_the_near_end_and_stays_where_it_is_when_nothing_else_can_be_picked()
    {
        if (NodeFixture.Run("CommandPaletteFixture") is not { } rules)
        {
            return;
        }

        var (first, last) = (Lit(rules, "first"), Lit(rules, "last"));
        var (alone, none, empty) = (Lit(rules, "onlyOne"), Lit(rules, "none"), Lit(rules, "empty"));

        Assert.Equal(0, first);
        Assert.Equal(3, last);
        Assert.Equal(1, alone);
        Assert.Equal(-1, none);
        Assert.Equal(-1, empty);
    }

    private static bool Says(JsonElement rules, string rule) => rules.GetProperty(rule).GetBoolean();

    private static int Lit(JsonElement rules, string rule) => rules.GetProperty(rule).GetInt32();
}
