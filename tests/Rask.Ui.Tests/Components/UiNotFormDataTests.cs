using Rask.Core;
using Rask.UiTests.Flux;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     The fields of a kit control the reader types or chooses in that hold none of a form's data say
///     <c>data-rask-unguarded</c>: a form that asks before it is left does not count them (<c>rask-leave.ts</c>).
///     A field whose text IS the value does not say it.
/// </summary>
public partial class UiNotFormDataTests : global::Rask.Core.RaskMarkup
{
    private const string Mark = "data-rask-unguarded";

    private static readonly string[] Words = ["Alpha", "Beta"];

    private static readonly Dictionary<string, (Func<Answers, Component> Draw, string Field)> NotData = new(StringComparer.Ordinal)
    {
        ["a listbox's search field"] = (a => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Options()], "[data-ui-select-search] input"),
        ["a search field that waits for a pause"] = (
            a => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Ui.SelectSearch.OnInput(_ => { }).Debounce(TimeSpan.FromMilliseconds(250)), Options()],
            "[data-ui-select-search] input"),
        ["a combobox's input"] = (a => Ui.Select.Bind(() => a.One).Combobox[Options()], "input[role=\"combobox\"]"),
        ["a pillbox's search field"] = (a => Ui.Pillbox.Bind(() => a.Many).Searchable()[Pills()], "[data-ui-pillbox-search] input"),
        ["a pillbox's own input"] = (a => Ui.Pillbox.Bind(() => a.Many).Combobox[Pills()], "input[data-ui-pillbox-input]"),
        ["a calendar's month list"] = (a => Ui.Calendar.Bind(() => a.Day).SelectableHeader().Locale("en-US").On(new DateOnly(2026, 1, 15)), "select"),
    };

    public static TheoryData<string> Names => [.. NotData.Keys];

    private static IEnumerable<Component> Options() => Words.Select(word => Ui.SelectOption.Key(word).Value(word)[word]);

    private static IEnumerable<Component> Pills() => Words.Select(word => Ui.PillboxOption.Key(word)[word]);

    [Theory]
    [MemberData(nameof(Names))]
    public void A_field_that_holds_none_of_the_forms_data_says_so(string name)
    {
        var answers = new Answers();

        var page = Page.Render(() => Form.Model(answers)[NotData[name].Draw(answers)]);

        Assert.All(page.FindAll(NotData[name].Field), field => Assert.NotNull(field.Attribute(Mark)));
        Assert.NotEmpty(page.FindAll(NotData[name].Field));
    }

    [Fact]
    public void A_field_whose_text_is_the_value_does_not()
    {
        var answers = new Answers();

        var page = Page.Render(() => Form.Model(answers)[
            Ui.Input.Bind(() => answers.Text),
            Ui.Autocomplete.Bind(() => answers.Text)[Words.Select(word => Ui.AutocompleteItem.Key(word)[word])],
            Ui.Select.Bind(() => answers.One)[Options()]
        ]);

        Assert.Empty(page.FindAll("[" + Mark + "]"));
        Assert.Equal(3, page.FindAll("input").Count + page.FindAll("select").Count);
    }

    private sealed class Answers
    {
        public string Text { get; set; } = string.Empty;

        public string? One { get; set; }

        public List<string> Many { get; set; } = [];

        public DateOnly Day { get; set; }
    }
}
