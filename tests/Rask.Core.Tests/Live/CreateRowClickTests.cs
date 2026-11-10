using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The press on a list's create row: sent while the row is on offer, refused while the list's search is out
///     (<c>aria-disabled="true"</c>, written by rask-list.ts), and refused again while the press before it is being made.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-clicks.ts</c> and <c>rask-loading.ts</c> in-process on Jint
///     (<c>CreateRowClickFixture.ts</c>). The list's own part — the mark, the keys, the list held open — is the
///     <c>CreateRowWaitsTests</c> E2E on the Server host and <c>UiKitSelectTests</c> on WebAssembly.
/// </remarks>
public sealed class CreateRowClickTests
{
    private static JsonElement Run() => ScriptFixture.Run("CreateRowClickFixture");

    [Fact]
    public void A_create_row_on_offer_is_pressed()
    {
        var root = Run();

        var offered = root.GetProperty("offered");

        Assert.True(offered.GetProperty("sent").GetBoolean());
    }

    [Fact]
    public void A_create_row_while_the_search_is_out_is_not_pressed_whoever_clicks_it()
    {
        var root = Run();

        var searching = root.GetProperty("searching");

        Assert.False(searching.GetProperty("sent").GetBoolean());
        Assert.True(searching.GetProperty("prevented").GetBoolean());
    }

    [Fact]
    public void Another_row_saying_aria_disabled_is_left_to_its_page()
    {
        var root = Run();

        var other = root.GetProperty("other");

        Assert.True(other.GetProperty("sent").GetBoolean());
    }

    [Fact]
    public void A_pressed_create_row_spins_at_once_and_takes_no_second_press_until_it_is_done()
    {
        var root = Run();

        var pressed = root.GetProperty("pressed");

        Assert.True(pressed.GetProperty("first").GetBoolean());
        Assert.True(pressed.GetProperty("spins").GetBoolean());
        Assert.Equal("true", pressed.GetProperty("busy").GetString());
        Assert.False(pressed.GetProperty("second").GetBoolean());
        Assert.True(pressed.GetProperty("third").GetBoolean());
    }
}
