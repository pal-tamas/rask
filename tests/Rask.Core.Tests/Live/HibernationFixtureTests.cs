using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     What a hidden Server tab answers when the server offers to let its session go: yes only when its resume record
///     carries everything that matters — driven in-process on Jint (<c>HibernationFixture.ts</c>).
/// </summary>
public sealed class HibernationFixtureTests
{
    private static JsonElement Run() => ScriptFixture.Run("HibernationFixture");

    [Fact]
    public void A_hidden_tab_holding_nothing_says_yes()
    {
        var result = Run();

        var answer = result.GetProperty("quiet");

        Assert.Equal(JsonValueKind.Null, answer.ValueKind);
    }

    [Theory]
    [InlineData("shown", "shown")]
    [InlineData("unsaved", "input")]
    [InlineData("media", "media")]
    [InlineData("interop", "interop")]
    [InlineData("busy", "busy")]
    [InlineData("waiting", "busy")]
    public void A_tab_holding_what_its_record_cannot_carry_says_not_now_and_why(string tab, string why)
    {
        var result = Run();

        var answer = result.GetProperty(tab).GetString();

        Assert.Equal(why, answer);
    }

    [Fact]
    public void A_tab_in_sight_says_so_first_and_an_edit_in_hand_comes_before_what_waits()
    {
        var result = Run();

        var shown = result.GetProperty("shownAndUnsaved").GetString();
        var unsaved = result.GetProperty("unsavedAndBusy").GetString();

        Assert.Equal("shown", shown);
        Assert.Equal("input", unsaved);
    }
}
