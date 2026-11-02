using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A bound select that waits holds its pick in the browser (<c>held</c>, <c>rask-list-held.ts</c>): its rows
///     carry their form value and no handler, and the answer travels in one bound field that waits for the next
///     action — Flux's <c>wire:model</c>, which sends nothing at the pick.
/// </summary>
public partial class UiSelectHeldTests : global::Rask.Core.RaskMarkup
{
    private const string Field = "[data-ui-select] > input[type=\"hidden\"]";

    private sealed class Trip
    {
        public string? Room { get; set; }

        public int Floor { get; set; } = 2;

        public List<string> Rooms { get; set; } = [];
    }

    private static IEnumerable<Component> Rooms() =>
    [
        Ui.SelectOption.Key("attic").Value("attic")["Attic"],
        Ui.SelectOption.Key("cellar").Value("cellar").SelectedLabel("The cellar")["Cellar"],
        Ui.SelectOption.Key("hall").Value("hall").Disabled()["Hall"],
    ];

    private static IEnumerable<string> NotTheCellar(string? room) => room == "cellar" ? ["Not the cellar."] : [];

    [Fact]
    public void A_bound_listbox_says_held_and_carries_its_answer_in_one_field_that_waits_for_the_next_action()
    {
        var trip = new Trip { Room = "attic" };

        var page = Page.Render(() => Ui.Select.Bind(() => trip.Room).Listbox[Rooms()]);

        Assert.Equal("held", page.Find("[data-ui-select]").Attribute("data-rask-list"));
        Assert.Equal("[\"attic\"]", page.Find(Field).Attribute("value"));
        Assert.Equal("action", page.Find(Field).Attribute("data-rask-bind-on"));
        Assert.Null(page.Find(Field).Attribute("name"));
        Assert.Empty(page.FindAll("[data-ui-option][data-rask-on-click]"));
        Assert.Equal("The cellar", page.Find("[data-rask-list-value=\"cellar\"]").Attribute("data-rask-list-says"));
        Assert.NotNull(page.Find("[data-ui-select-button] [data-rask-list-shows]"));
        Assert.NotNull(page.Find("template[data-rask-list-faces]"));
    }

    [Fact]
    public void A_live_list_one_the_page_holds_and_one_it_searches_are_not_held()
    {
        var trip = new Trip();

        var page = Page.Render(() => Fragment[
            Div.Id("live")[Ui.Select.Bind(() => trip.Room).Listbox.Live()[Rooms()]],
            Div.Id("searched")[Ui.Select.Bind(() => trip.Room).Listbox.Searchable().Filter(false)[Ui.SelectSearch.OnInput(_ => { }), Rooms()]],
            Div.Id("held-by-page")[Ui.Select.Value(trip.Room).OnChange(room => trip.Room = room).Listbox[Rooms()]],
            Div.Id("many-live")[Ui.Select.Bind(() => trip.Rooms).Listbox.Multiple().Live()[Ui.SelectOption["attic"], Ui.SelectOption["cellar"]]],
            Div.Id("many-by-page")[Ui.Select.Values(trip.Rooms).OnChange(rooms => trip.Rooms = [.. rooms]).Listbox.Multiple()[Ui.SelectOption["attic"]]],
            Div.Id("pills-live")[Ui.Pillbox.Bind(() => trip.Rooms).Live()[Ui.PillboxOption["attic"], Ui.PillboxOption["cellar"]]],
            Div.Id("pills-searched")[Ui.Pillbox.Bind(() => trip.Rooms).Combobox.Filter(false)[Ui.PillboxInput.OnInput(_ => { }), Ui.PillboxOption["attic"]]]
        ]);

        Assert.Empty(page.FindAll("[data-rask-list-value]"));
        Assert.Empty(page.FindAll("template[data-rask-list-faces]"));
        Assert.All(page.FindAll("[data-rask-list]"), list => Assert.DoesNotContain("held", list.Attribute("data-rask-list"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_pick_the_browser_held_is_written_with_the_next_action_and_shown_until_then()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox.Placeholder("Choose…")[Rooms()]);

        await HeldList.Pick(page, "Cellar");
        var (written, shown) = (trip.Room, page.TextOf("[data-ui-select-button] [data-value]"));
        await Acting.Act(page);

        Assert.Null(written);
        Assert.Equal("The cellar", shown);
        Assert.Equal("cellar", trip.Room);
        Assert.Equal("[\"cellar\"]", page.Find(Field).Attribute("value"));
    }

    [Fact]
    public async Task A_clear_the_browser_held_empties_the_answer_with_the_next_action()
    {
        var trip = new Trip { Room = "attic" };
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox.Clearable()[Rooms()]);

        await HeldList.Clear(page);
        await Acting.Act(page);

        Assert.Null(trip.Room);
        Assert.Equal("[]", page.Find(Field).Attribute("value"));
    }

    [Fact]
    public async Task A_form_value_no_row_posts_and_a_disabled_row_are_no_answer_of_the_readers()
    {
        var trip = new Trip { Room = "attic" };
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox[Rooms()]);
        var field = page.Find(Field).Attribute("data-rask-on-change")!;

        await page.Invoke(field, "{\"value\":\"[\\\"garage\\\",\\\"hall\\\"]\"}");
        await Acting.Act(page);

        Assert.Null(trip.Room);
    }

    [Fact]
    public async Task A_field_that_is_not_an_array_of_form_values_holds_no_answer()
    {
        var trip = new Trip { Room = "attic" };
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Room).Listbox[Rooms()]);

        await page.On(Field).Change("{not json");
        await Acting.Act(page);

        Assert.Null(trip.Room);
    }

    [Fact]
    public async Task A_typed_answer_is_found_by_the_form_value_its_row_posts()
    {
        var trip = new Trip();
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Floor).Combobox[
            Ui.SelectOption.Value(1)["First"], Ui.SelectOption.Value(3)["Third"]
        ]);

        await HeldList.Pick(page, "Third");
        await Acting.Act(page);

        Assert.Equal(3, trip.Floor);
    }

    [Fact]
    public async Task A_submit_validates_the_pick_the_browser_held()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip).OnSubmit(() => { })[
            Ui.Select.Bind(() => trip.Room).Listbox.Validate(NotTheCellar).Label("Room")[Rooms()]
        ]);

        await HeldList.Pick(page, "Cellar");
        var atThePick = page.Html;
        await page.On("form").Submit();

        Assert.DoesNotContain("Not the cellar.", atThePick, StringComparison.Ordinal);
        Assert.Contains("Not the cellar.", page.Html, StringComparison.Ordinal);
        Assert.Equal("cellar", trip.Room);
    }

    [Fact]
    public async Task A_message_under_a_held_select_goes_at_the_first_pick_after_it()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip).OnSubmit(() => { })[
            Ui.Select.Bind(() => trip.Room).Listbox.Validate(NotTheCellar).Label("Room")[Rooms()]
        ]);
        await HeldList.Pick(page, "Cellar");
        await page.On("form").Submit();
        var edit = page.Find(Field).Attribute("data-rask-on-edit");

        await page.Invoke(edit!);

        Assert.NotNull(edit);
        Assert.DoesNotContain("Not the cellar.", page.Html, StringComparison.Ordinal);
        Assert.Null(page.Find(Field).Attribute("data-rask-on-edit"));
    }

    private static IEnumerable<Component> Pills() =>
    [
        Ui.PillboxOption.Value("attic")["Attic"],
        Ui.PillboxOption.Value("cellar")["Cellar"],
    ];

    private static IEnumerable<string> NoCellar(ICollection<string> rooms) => rooms.Contains("cellar") ? ["Not the cellar."] : [];

    [Fact]
    public async Task A_bound_multiple_listbox_holds_its_picks_in_the_order_they_were_made_until_the_next_action()
    {
        var trip = new Trip { Rooms = ["cellar"] };
        var page = Acting.Render(() => Ui.Select.Bind(() => trip.Rooms).Listbox.Multiple().Placeholder("Choose…")[Rooms()]);
        var kind = page.Find("[data-ui-select]").Attribute("data-rask-list");
        var count = page.Find("template[data-rask-list-faces] [data-rask-list-count]").Attribute("data-rask-list-count");

        await HeldList.PickAll(page, "Cellar", "Attic");
        var written = trip.Rooms.ToList();
        await Acting.Act(page);

        Assert.Equal("multiple held", kind);
        Assert.Equal("selected", count);
        Assert.Equal(["cellar"], written);
        Assert.Equal(["cellar", "attic"], trip.Rooms);
        Assert.Equal("[\"cellar\",\"attic\"]", page.Find(Field).Attribute("value"));
        Assert.Equal("2 selected", page.TextOf("[data-ui-select-button] [data-rask-list-shows]"));
    }

    [Fact]
    public void A_named_multiple_listbox_posts_its_answers_from_a_box_the_browser_fills()
    {
        var trip = new Trip { Rooms = ["attic", "cellar"] };

        var page = Page.Render(() => Ui.Select.Bind(() => trip.Rooms).Listbox.Multiple().Name("rooms")[Rooms()]);

        var box = page.Find("[data-rask-list-posts]");
        Assert.Equal("rooms", box.Attribute("data-name"));
        Assert.NotNull(box.Attribute("data-rask-opaque"));
        Assert.Equal(["attic", "cellar"], page.FindAll("[data-rask-list-posts] input[name=\"rooms\"]").Select(field => field.Attribute("value")));
    }

    [Fact]
    public void A_bound_pillbox_draws_its_pills_in_a_box_of_the_browsers_with_a_blank_pill_to_fill()
    {
        var trip = new Trip { Rooms = ["attic"] };

        var page = Page.Render(() => Ui.Pillbox.Bind(() => trip.Rooms).Placeholder("Rooms")[Pills()]);

        Assert.Contains("held", page.Find("[data-ui-pillbox]").Attribute("data-rask-list"), StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-ui-pillbox-trigger] [data-rask-list-pills]").Attribute("data-rask-opaque"));
        Assert.NotNull(page.Find("[data-ui-pillbox-trigger] [data-rask-list-blank]").Attribute("data-rask-opaque"));
        Assert.Empty(page.FindAll("[data-rask-list-remove][data-rask-on-click]"));
        Assert.Equal("Cellar", page.Find("[data-rask-list-value=\"cellar\"]").Attribute("data-rask-list-says"));
        Assert.NotNull(page.Find("template[data-rask-list-faces] [data-rask-list-pill] [data-rask-list-remove]"));
        Assert.Equal("Rooms", page.TextOf("template[data-rask-list-faces] [data-ui-pillbox-placeholder]"));
        Assert.Equal("Attic", page.TextOf("[data-ui-pillbox-trigger] [data-value=\"attic\"]"));
    }

    [Fact]
    public async Task A_pill_the_browser_added_and_one_it_took_off_are_written_with_the_next_action()
    {
        var trip = new Trip { Rooms = ["attic"] };
        var page = Acting.Render(() => Ui.Pillbox.Bind(() => trip.Rooms)[Pills()]);

        await HeldList.PickAll(page, "Attic", "Cellar");
        var picked = trip.Rooms.ToList();
        await Acting.Act(page);
        var added = trip.Rooms.ToList();
        await HeldList.TakeOff(page, "attic", "cellar");
        await Acting.Act(page);

        Assert.Equal(["attic"], picked);
        Assert.Equal(["attic", "cellar"], added);
        Assert.Equal(["cellar"], trip.Rooms);
        Assert.Equal(["cellar"], page.FindAll("[data-ui-pillbox-trigger] [data-value]").Select(pill => pill.Attribute("data-value")));
    }

    [Fact]
    public async Task A_submit_validates_the_pills_the_browser_held()
    {
        var trip = new Trip();
        var page = Page.Render(() => Form.Model(trip).OnSubmit(() => { })[
            Ui.Pillbox.Bind(() => trip.Rooms).Validate(NoCellar).Label("Rooms")[Pills()]
        ]);

        await HeldList.PickAll(page, "Cellar");
        var atThePick = page.Html;
        await page.On("form").Submit();

        Assert.DoesNotContain("Not the cellar.", atThePick, StringComparison.Ordinal);
        Assert.Contains("Not the cellar.", page.Html, StringComparison.Ordinal);
        Assert.Equal(["cellar"], trip.Rooms);
    }

    [Theory]
    [InlineData(new string[0], "[]")]
    [InlineData(new[] { "" }, "[\"\"]")]
    [InlineData(new[] { "a\"b", "c" }, "[\"a\\u0022b\",\"c\"]")]
    public void Form_values_are_written_as_a_json_array_and_read_back(string[] values, string field)
    {
        var written = UiFormValues.Write(values);
        var read = UiFormValues.Read(written);

        Assert.Equal(field, written);
        Assert.Equal(values, read);
    }
}
