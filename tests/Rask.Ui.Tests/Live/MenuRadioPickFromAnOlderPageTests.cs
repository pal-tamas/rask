using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     Rows of a menu's radio group picked faster than the page answers: every press is read from the page as
///     loaded, and each one the page hears is a render of the group — so the last arrives several pages late.
/// </summary>
/// <remarks>
///     A row's press closed over the group one render resolved — the value chosen then — so no two renders made the
///     same handler: the fourth quick pick ran nothing (<c>Component.StaleEvents</c>).
/// </remarks>
public sealed partial class MenuRadioPickFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_fifth_row_picked_before_the_page_answered_the_others_is_the_groups_value()
    {
        var who = "a";
        using var session = new OlderPage(new StubComponent(() => Ui.MenuRadioGroup.Value(who).OnChange(w => who = w)[
            Ui.MenuRadio.Value("a")["A"], Ui.MenuRadio.Value("b")["B"], Ui.MenuRadio.Value("c")["C"], Ui.MenuRadio.Value("d")["D"], Ui.MenuRadio.Value("e")["E"]]));
        var ids = session.Ids("click");

        var heard = true;
        foreach (var id in ids.Skip(1))
        {
            heard = await session.PressedAsync(id);
        }

        Assert.True(heard);
        Assert.Equal("e", who);
    }

    [Fact]
    public async Task A_fifth_row_picked_before_the_page_answered_the_others_is_written_to_the_model()
    {
        var m = new Plan();
        using var session = new OlderPage(new StubComponent(() => Ui.MenuRadioGroup.Bind(() => m.Value).Live()[
            Ui.MenuRadio.Value("a")["A"], Ui.MenuRadio.Value("b")["B"], Ui.MenuRadio.Value("c")["C"], Ui.MenuRadio.Value("d")["D"], Ui.MenuRadio.Value("e")["E"]]));
        var ids = session.Ids("click");

        var heard = true;
        foreach (var id in ids.Skip(1))
        {
            heard = await session.PressedAsync(id);
        }

        Assert.True(heard);
        Assert.Equal("e", m.Value);
    }

    private sealed class Plan
    {
        public string Value { get; set; } = "a";
    }
}
