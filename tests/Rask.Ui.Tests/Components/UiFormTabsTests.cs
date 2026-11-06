using Rask.Core;
using Rask.Core.Forms;
using Rask.Testing;
using Rask.Wire;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A long form's tabs: each counts the invalid fields it holds in a red badge, and a refused save opens the
///     first tab holding one.
/// </summary>
public partial class UiFormTabsTests : global::Rask.Core.RaskMarkup
{
    private const string Required = "Required.";

    [Fact]
    public async Task Each_tab_counts_the_invalid_fields_it_holds()
    {
        var person = new TabbedPerson();
        var page = Acting.Render(() => Editor(person));

        await page.On("form").Submit();

        Assert.Equal(("2", "2", "1"), (Count(page, "person"), Count(page, "address"), Count(page, "rows")));
    }

    [Fact]
    public void A_form_with_no_messages_shows_no_count_and_its_first_tab()
    {
        var person = new TabbedPerson();
        var page = Acting.Render(() => Editor(person));

        var counts = page.FindAll("[data-ui-form-tab-count]");

        Assert.Empty(counts);
        Assert.Equal("Person", Selected(page));
        Assert.Equal(3, page.FindAll("[role=\"tabpanel\"]").Count);
    }

    [Fact]
    public async Task The_count_is_a_red_small_badge_and_a_sentence_for_a_screen_reader()
    {
        var person = new TabbedPerson();
        var page = Acting.Render(() => Editor(person));

        await page.On("form").Submit();

        var badge = page.Find("[data-ui-tab][data-rask-tab=\"person\"] [data-ui-badge]");
        Assert.Contains("text-red-700", badge.Attribute("class"), StringComparison.Ordinal);
        Assert.Equal("true", page.Find("[data-ui-tab][data-rask-tab=\"person\"] [aria-hidden=\"true\"]").Attribute("aria-hidden"));
        Assert.Equal("Fields to correct: 2", page.TextOf("[data-ui-tab][data-rask-tab=\"person\"] .sr-only"));
    }

    [Fact]
    public async Task A_corrected_field_takes_one_off_its_tabs_count_with_the_next_action()
    {
        var person = new TabbedPerson();
        var page = Acting.Render(() => Editor(person));
        await page.On("form").Submit();

        await page.On("#person-name").Change("Ada");
        await Acting.Act(page);

        Assert.Equal("1", Count(page, "person"));
        Assert.Equal("2", Count(page, "address"));
    }

    [Fact]
    public async Task A_tab_whose_fields_are_all_corrected_shows_no_count_after_the_next_action()
    {
        var person = new TabbedPerson();
        var page = Acting.Render(() => Editor(person));
        await page.On("form").Submit();

        await page.On("#row-0-label").Change("Front");
        await Acting.Act(page);

        Assert.Null(Count(page, "rows"));
    }

    [Fact]
    public async Task A_refused_save_opens_the_first_tab_holding_an_invalid_field()
    {
        var person = new TabbedPerson { Name = "Ada", Email = "ada@example.com" };
        var page = Acting.Render(() => Editor(person));

        await page.On("form").Submit();

        Assert.Equal("Address", Selected(page));
        Assert.False(page.Find("#" + PanelOf(page, "address")).Attributes.ContainsKey("hidden"));
    }

    [Fact]
    public async Task A_refused_save_opens_the_first_failing_tab_again_wherever_the_reader_went()
    {
        var person = new TabbedPerson { Name = "Ada", Email = "ada@example.com" };
        var page = Acting.Render(() => Editor(person));
        await page.On("form").Submit();

        await page.On("[role=\"tablist\"] > input[type=\"hidden\"]").Change("rows");
        await page.On("form").Submit();

        Assert.Equal("Address", Selected(page));
    }

    [Fact]
    public async Task A_save_the_handler_refuses_with_field_failures_opens_the_tab_holding_them()
    {
        var person = Valid();
        var refusal = new RefusedException(new FieldFailure("Taken.", ["Email"]));
        var page = Acting.Render(() => Editor(person, refusal));
        await page.On("[role=\"tablist\"] > input[type=\"hidden\"]").Change("rows");

        await page.On("form").Submit();

        Assert.Equal("Person", Selected(page));
        Assert.Equal("1", Count(page, "person"));
    }

    [Fact]
    public async Task A_field_no_tab_holds_is_counted_by_none_and_opens_none()
    {
        var person = Valid();
        person.Notes = "";
        var page = Acting.Render(() => Editor(person));
        await page.On("[role=\"tablist\"] > input[type=\"hidden\"]").Change("rows");

        await page.On("form").Submit();

        Assert.Empty(page.FindAll("[data-ui-form-tab-count]"));
        Assert.Equal("Rows", Selected(page));
        Assert.Contains(Required, page.TextOf("#notes-error"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_message_taken_off_outside_any_action_asks_the_row_to_draw_its_counts_again()
    {
        var person = new TabbedPerson { Address = new TabbedAddress { Street = "Main" } };
        EditContext? form = null;
        var tabs = Ui.FormTabs;
        var page = Acting.Render(() => Form.Model(person)[
            Test.EditContextProbe(captured => form = captured),
            tabs[
                Ui.FormTab.Name("address").Label("Address").Holds(() => person.Address)[
                    Ui.Input.Bind(() => person.Address.City).Id("address-city").Validate(Filled)
                ]
            ]
        ]);
        await page.On("form").Submit();
        var before = (Count(page, "address"), tabs.IsRenderRequestedForTest);

        form!.ClearMessages(new FieldIdentifier(person.Address, nameof(TabbedAddress.City)));

        Assert.Equal(("1", false), before);
        Assert.True(tabs.IsRenderRequestedForTest);
    }

    private static string? Selected(Page page) => page.Find("[role=\"tab\"][aria-selected=\"true\"]").TextContent.Trim() switch
    {
        var text when text.StartsWith("Person", StringComparison.Ordinal) => "Person",
        var text when text.StartsWith("Address", StringComparison.Ordinal) => "Address",
        var text when text.StartsWith("Rows", StringComparison.Ordinal) => "Rows",
        var text => text,
    };

    // The id of the panel a tab controls, as its aria-controls names it.
    private static string PanelOf(Page page, string tab) => page.Find($"[data-rask-tab=\"{tab}\"]").Attribute("aria-controls")!;

    private static string? Count(Page page, string tab) =>
        page.FindAll($"[data-rask-tab=\"{tab}\"] [data-ui-form-tab-count]") is [var count] ? count.Attribute("data-ui-form-tab-count") : null;

    private static TabbedPerson Valid() => new()
    {
        Name = "Ada",
        Email = "ada@example.com",
        Address = new TabbedAddress { Street = "Main", City = "Pécs" },
        Rows = [new TabbedRow { Label = "Front" }],
        Notes = "None",
    };

    private static Component Editor(TabbedPerson person, Exception? refusal = null) =>
        Form.Model(person).OnSubmit(_ => refusal is null ? Task.CompletedTask : Task.FromException(refusal))[
            Ui.FormTabs[
                Ui.FormTab.Name("person").Label("Person").Holds(() => person.Name, () => person.Email)[
                    Ui.Input.Bind(() => person.Name).Id("person-name").Validate(Filled),
                    Ui.Input.Bind(() => person.Email).Id("person-email").Validate(Filled)
                ],
                Ui.FormTab.Name("address").Label("Address").Holds(() => person.Address)[
                    Ui.Input.Bind(() => person.Address.Street).Id("address-street").Validate(Filled),
                    Ui.Input.Bind(() => person.Address.City).Id("address-city").Validate(Filled)
                ],
                Ui.FormTab.Name("rows").Label("Rows").Holds(() => person.Rows)[
                    person.Rows.Select((row, i) => Ui.Input.Key(i).Bind(() => row.Label).Id($"row-{i}-label").Validate(Filled))
                ]
            ],
            Ui.Field[
                Ui.Label["Notes"],
                Ui.Textarea.Bind(() => person.Notes).Id("notes").Validate(Filled),
                Ui.Error.Id("notes-error")
            ],
            Ui.Button.Submit["Save"]
        ];

    private static IEnumerable<string> Filled(string? value) => string.IsNullOrEmpty(value) ? [Required] : [];

    private sealed class RefusedException(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }
}

public sealed class TabbedPerson
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public TabbedAddress Address { get; set; } = new();
    public List<TabbedRow> Rows { get; set; } = [new()];
    public string Notes { get; set; } = "";
}

public sealed class TabbedAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
}

public sealed class TabbedRow
{
    public string Label { get; set; } = "";
}
