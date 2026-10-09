using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     Picking in a multiple select changes what its button says — the placeholder, the one option, a count. Each
///     of those is answered with a diff: a full page here cost 85 KB a pick and lost what was typed next.
/// </summary>
public partial class SelectPickIsADiffTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

    private static readonly string[] Pistols = ["Glock", "Beretta", "Walther"];

    private List<string> _picked = [];

    private Component Select(bool clearable, string? name) =>
        Ui.Select.Values(_picked).OnChange(values => _picked = [.. values]).Listbox.Multiple().Clearable(clearable).Name(name)
            .Placeholder("Choose…")[Pistols.Select(pistol => Ui.SelectOption.Key(pistol)[pistol])];

    private static Task Pick(Page page, string name) =>
        page.On($"[data-ui-option]:has-text(\"{name}\")").Click();

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "pistols")]
    public async Task Every_pick_from_none_to_two_and_back_is_answered_with_a_diff(bool clearable, string? name)
    {
        var page = Page.Render(() => Select(clearable, name));
        await page.On("[popover]").Raise("toggle", Opened);

        var said = new List<string>();
        foreach (var pistol in new[] { "Glock", "Beretta", "Beretta", "Glock" })
        {
            await Pick(page, pistol);
            said.Add(page.TextOf("[data-ui-select-button]"));
        }

        Assert.Equal(["Glock", "2 selected", "Glock", "Choose…"], said);
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task The_clear_button_arriving_leaves_every_option_wired_to_the_handler_it_had()
    {
        var page = Page.Render(() => Select(clearable: true, name: null));
        await page.On("[popover]").Raise("toggle", Opened);
        var before = page.FindAll("[data-ui-option]").Select(option => option.Attribute("data-rask-on-click")).ToList();

        await Pick(page, "Glock");

        Assert.True(page.Exists("[aria-label=\"Clear selected\"]"));
        Assert.Equal(before, page.FindAll("[data-ui-option]").Select(option => option.Attribute("data-rask-on-click")).ToList());
    }
}
