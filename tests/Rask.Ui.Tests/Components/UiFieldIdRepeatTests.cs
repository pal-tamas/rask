using Rask.Core;
using Rask.UiTests.Live;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Two fields on one page that derive the same id (#1378): the first keeps it, the next takes it with a number,
///     and each field's label, error and description follow its own control.
/// </summary>
public partial class UiFieldIdRepeatTests : global::Rask.Core.RaskMarkup
{
    private readonly Person _first = new();
    private readonly Person _second = new();
    private bool _dialog;
    private int _count;

    private Component TwoForms() =>
    [
        Ui.Button.Id("count").OnClick(() => _count++)[$"Pressed {_count}"],
        Ui.Button.Id("open").OnClick(() => _dialog = !_dialog)["Open"],
        _dialog ? Div.Id("late")[Ui.Input.Key("late").Bind(() => _second.Name).Label("Late name")] : null,
        Form.Model(_first)[Ui.Input.Bind(() => _first.Name).Label("Name").Description("Who you are.")],
        Form.Model(_second)[Ui.Input.Bind(() => _second.Name).Label("Merged name").Description("Who they are.")],
    ];

    [Fact]
    public void Two_forms_with_a_Name_field_give_the_second_its_own_id_and_its_parts_follow_it()
    {
        var page = Page.Render(TwoForms);

        var inputs = page.FindAll("input[data-ui-control]");
        var labels = page.FindAll("label[for]");

        Assert.Equal(["f-name", "f-name-2"], inputs.Select(i => i.Id));
        Assert.Equal(["f-name", "f-name-2"], labels.Select(l => l.Attribute("for")));
        Assert.Equal("f-name-2-description", inputs[1].Attribute("aria-describedby"));
        Assert.True(page.Exists("#f-name-2-description"));
        Assert.True(page.Exists("#f-name-description"));
    }

    [Fact]
    public void An_id_the_call_site_gives_is_never_changed()
    {
        var model = new Person();

        var page = Page.Render(() => (Component)Div[
            Ui.Input.Bind(() => model.Name).Label("Name"),
            Ui.Input.Bind(() => model.Name).Id("f-name").Label("Again"),
            Ui.Input.Of<string>().Id("own").Label("Own")]);

        Assert.Equal(["f-name", "f-name", "own"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
    }

    [Fact]
    public void A_derived_id_steps_past_one_the_call_site_gave_earlier_on_the_page()
    {
        var model = new Person();

        var page = Page.Render(() => (Component)Div[
            Ui.Input.Of<string>().Id("f-name").Label("Given"),
            Ui.Input.Bind(() => model.Name).Label("Name")]);

        Assert.Equal(["f-name", "f-name-2"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
    }

    [Fact]
    public async Task A_page_that_renders_again_keeps_every_field_its_id()
    {
        var page = Page.Render(TwoForms);

        await page.On("#count").Click();
        await page.On("#count").Click();

        Assert.Equal(2, _count);
        Assert.Equal(["f-name", "f-name-2"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
        Assert.DoesNotContain(DiffGuardAttribute.Frames(), frame => frame.Contains("f-name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_field_that_comes_in_later_above_the_others_takes_a_new_number_and_they_keep_theirs()
    {
        var page = Page.Render(TwoForms);

        await page.On("#open").Click();

        Assert.Equal(["f-name-3", "f-name", "f-name-2"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
        Assert.Equal("f-name-3", page.Find("#late input[data-ui-control]").Id);
        Assert.Equal("f-name-3", page.Find("#late label").Attribute("for"));
        Assert.Equal(["f-name-3", "f-name", "f-name-2"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task A_field_that_leaves_frees_its_id_for_the_next_one()
    {
        var page = Page.Render(TwoForms);
        await page.On("#open").Click();

        await page.On("#open").Click();
        await page.On("#open").Click();

        Assert.Equal("f-name-3", page.Find("#late input[data-ui-control]").Id);
        Assert.Equal(["f-name-3", "f-name", "f-name-2"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
    }

    [Fact]
    public void A_list_of_rows_each_with_a_Name_field_numbers_them_in_order()
    {
        var rows = new[] { new Person(), new Person(), new Person() };

        var page = Page.Render(() => (Component)Div[
            rows.Select((row, i) => Ui.Input.Key(i).Bind(() => row.Name).Label("Name"))]);

        Assert.Equal(["f-name", "f-name-2", "f-name-3"], page.FindAll("input[data-ui-control]").Select(i => i.Id));
        Assert.Equal(["f-name", "f-name-2", "f-name-3"], page.FindAll("label[for]").Select(l => l.Attribute("for")));
    }

    [Fact]
    public void Two_renders_of_the_same_page_from_scratch_agree_on_every_id()
    {
        var first = Page.Render(TwoForms).Render();

        var second = Page.Render(TwoForms).Render();

        Assert.Equal(Ids(first), Ids(second));
        Assert.Contains("id=\"f-name-2\"", second, StringComparison.Ordinal);
    }

    private static string[] Ids(string html) =>
        [.. System.Text.RegularExpressions.Regex.Matches(html, "id=\"(f-[^\"]*)\"").Select(m => m.Groups[1].Value)];

    private sealed class Person
    {
        public string? Name { get; set; }
    }
}
