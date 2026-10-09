using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Rask.UiTests.Flux;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     Every control the kit binds, inside a form as an app writes one — between two other bound fields, each in
///     a <c>Ui.Field</c> with a <c>Ui.Error</c> slot, under a layout that places the toasts and the leave dialog —
///     with its value going from nothing to one, to several, to fewer and back to nothing. Each step is a diff.
/// </summary>
/// <remarks>
///     <see cref="KitStateChangesAreDiffsTests" /> turns one prop on and off between two paragraphs. An app that
///     reported whole-page replies had the control BOUND and inside a <c>Form</c>, where it also posts its
///     answer, takes its id from the bound member and is described by the field's error slot — none of which the
///     bare control has. So the same walk is taken here with all of that around it.
/// </remarks>
public partial class FormBoundControlsAreDiffsTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Today = new(2026, 10, 9);

    private static readonly string[] Words = ["Alpha", "Beta", "Gamma"];

    private static readonly Dictionary<string, (Func<Answers, Component> Draw, Action<Answers>[] Steps)> Controls = new(StringComparer.Ordinal)
    {
        ["input"] = (a => Ui.Input.Bind(() => a.Text), Texts()),
        ["input, clearable"] = (a => Ui.Input.Bind(() => a.Text).Clearable(), Texts()),
        ["number input"] = (a => Ui.Input.Bind(() => a.Number).Type(InputType.Number), [a => a.Number = 1, a => a.Number = 600, a => a.Number = 6, a => a.Number = null]),
        ["textarea"] = (a => Ui.Textarea.Bind(() => a.Text), Texts()),
        ["checkbox"] = (a => Ui.Checkbox.Bind(() => a.Agree).Label("Agree"), [a => a.Agree = true, a => a.Agree = false, a => a.Agree = true, a => a.Agree = null]),
        ["switch"] = (a => Ui.Switch.Bind(() => a.Alerts).Label("Alerts"), [a => a.Alerts = true, a => a.Alerts = false]),
        ["checkbox group"] = (a => Ui.CheckboxGroup.Bind(() => a.Many)[Words.Select(word => Ui.Checkbox.Key(word).Value(word).Label(word))], Manys()),
        ["radio group"] = (a => Ui.RadioGroup.Bind(() => a.One).Name("one")[Words.Select(word => Ui.Radio.Key(word).Value(word).Label(word))], Ones()),
        ["native select"] = (a => Ui.Select.Bind(() => a.One).Placeholder("Choose…")[Options()], Ones()),
        ["listbox"] = (a => Ui.Select.Bind(() => a.One).Listbox.Placeholder("Choose…").Name("one")[Options()], Ones()),
        ["listbox, clearable"] = (a => Ui.Select.Bind(() => a.One).Listbox.Clearable().Placeholder("Choose…")[Options()], Ones()),
        ["listbox, searchable"] = (a => Ui.Select.Bind(() => a.One).Listbox.Searchable().Placeholder("Choose…")[Options()], Ones()),
        ["combobox"] = (a => Ui.Select.Bind(() => a.One).Combobox.Clearable().Placeholder("Choose…").Name("one")[Options()], Ones()),
        ["multiple listbox"] = (a => Ui.Select.Bind(() => a.Many).Listbox.Multiple().Placeholder("Choose…")[Options()], Manys()),
        ["multiple listbox, named"] = (a => Ui.Select.Bind(() => a.Many).Listbox.Multiple().Placeholder("Choose…").Name("many")[Options()], Manys()),
        ["multiple listbox, clearable"] = (a => Ui.Select.Bind(() => a.Many).Listbox.Multiple().Clearable().Placeholder("Choose…").Name("many")[Options()], Manys()),
        ["multiple listbox, searchable"] = (a => Ui.Select.Bind(() => a.Many).Listbox.Multiple().Searchable().Placeholder("Choose…")[Options()], Manys()),
        ["pillbox"] = (a => Ui.Pillbox.Bind(() => a.Many).Placeholder("Choose…")[Pills()], Manys()),
        ["pillbox, combobox"] = (a => Ui.Pillbox.Bind(() => a.Many).Combobox.Placeholder("Choose…")[Pills()], Manys()),
        ["pillbox, clearable"] = (a => Ui.Pillbox.Bind(() => a.Many)[Ui.PillboxTrigger.Placeholder("Choose…").Clearable(), Pills()], Manys()),
        ["autocomplete"] = (a => Ui.Autocomplete.Bind(() => a.Text).Clearable()[Words.Select(word => Ui.AutocompleteItem.Key(word)[word])], Texts()),
        ["date picker"] = (a => Ui.DatePicker.Bind(() => a.Day).Locale("en-US").On(Today), Days()),
        ["date picker, range"] = (a => Ui.DatePicker.Range.Bind(() => a.Stay).Locale("en-US").On(Today), Stays()),
        ["calendar"] = (a => Ui.Calendar.Bind(() => a.Day).Locale("en-US").On(Today), Days()),
        ["calendar, multiple"] = (a => Ui.Calendar.Multiple.Bind(() => a.Days).Locale("en-US").On(Today),
        [
            a => a.Days = [Today], a => a.Days = [Today, Today.AddDays(1), Today.AddDays(3)], a => a.Days = [Today.AddDays(3)], a => a.Days = [],
        ]),
        ["calendar, range"] = (a => Ui.Calendar.Range.Bind(() => a.Stay).Locale("en-US").On(Today), Stays()),
        ["time picker"] = (a => Ui.TimePicker.Bind(() => a.At).Clearable(),
        [
            a => a.At = new TimeOnly(9, 30), a => a.At = new TimeOnly(14, 0), a => a.At = null,
        ]),
        ["slider"] = (a => Ui.Slider.Bind(() => a.Volume), [a => a.Volume = 40, a => a.Volume = 100, a => a.Volume = 0]),
        ["rating"] = (a => Ui.Rating.Bind(() => a.Stars).Group("stars").Label("Stars").Max(5), [a => a.Stars = 1, a => a.Stars = 5, a => a.Stars = 2, a => a.Stars = 0]),
        ["otp"] = (a => Ui.Otp.Bind(() => a.Text).Length(6), [a => a.Text = "1", a => a.Text = "123456", a => a.Text = "12", a => a.Text = string.Empty]),
        ["editor"] = (a => Ui.Editor.Bind(() => a.Body), [a => a.Body = "<p>One</p>", a => a.Body = "<p>One</p><p>Two</p>", a => a.Body = null]),
    };

    public static TheoryData<string> Names => [.. Controls.Keys];

    private static Action<Answers>[] Texts() => [a => a.Text = "A", a => a.Text = "Alabama", a => a.Text = "Al", a => a.Text = string.Empty];

    private static Action<Answers>[] Ones() => [a => a.One = "Alpha", a => a.One = "Gamma", a => a.One = null];

    private static Action<Answers>[] Manys() =>
    [
        a => a.Many = ["Alpha"], a => a.Many = ["Alpha", "Beta", "Gamma"], a => a.Many = ["Beta"], a => a.Many = [],
    ];

    private static Action<Answers>[] Days() => [a => a.Day = Today, a => a.Day = Today.AddDays(40), a => a.Day = null];

    private static Action<Answers>[] Stays() =>
    [
        a => a.Stay = new UiDateRange(Today, Today), a => a.Stay = new UiDateRange(Today, Today.AddDays(40)), a => a.Stay = null,
    ];

    private static IEnumerable<Component> Options() => Words.Select(word => Ui.SelectOption.Key(word).Value(word)[word]);

    private static IEnumerable<Component> Pills() => Words.Select(word => Ui.PillboxOption.Key(word)[word]);

    // The form as the reporting app writes it: a routed page's content between a layout's header and its
    // toasts and leave dialog.
    private static Page Draw(Answers answers, Func<Answers, Component> control)
    {
        var services = new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider();

        return Page.Render(
            () => Div[
                Header[Nav[A.Href("/")["Home"]]],
                Main[
                    H1["Ranges"],
                    Form.Model(answers).OnSubmit(() => { }).ConfirmLeave("Leave without saving?")[
                        Ui.Field[Ui.Label.Badge("Required")["Before"], Ui.Input.Bind(() => answers.Before).ShowValidation(false), Ui.Error],
                        Ui.Field[Ui.Label["Under test"], control(answers), Ui.Error],
                        Ui.Field[Ui.Label["After"], Ui.Input.Bind(() => answers.After).Type(InputType.Number).ShowValidation(false), Ui.Error],
                        Ui.Button.Primary.Submit["Save"]
                    ]
                ],
                Footer["after"],
                Ui.Toast,
                Ui.ConfirmLeave
            ],
            services);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_bound_control_in_a_form_goes_through_its_values_with_a_diff_each_time(string name)
    {
        var answers = new Answers();
        var page = Draw(answers, Controls[name].Draw);

        foreach (var step in Controls[name].Steps)
        {
            step(answers);
            page.Render();
        }

        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Every_pick_in_a_form_bound_multiple_listbox_is_a_diff_while_its_rule_fails_and_passes(bool named, bool ruled)
    {
        var answers = new Answers();
        var page = Draw(answers, a =>
        {
            var select = Ui.Select.Bind(() => a.Many).Listbox.Multiple().Placeholder("Choose…").Name(named ? "many" : null);
            return (ruled ? select.ShowValidation(false).Validate(many => many.Count == 1 ? [] : ["Pick exactly one"]) : select)[Options()];
        });
        await page.On("[popover][data-ui-options]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        var said = new List<string>();
        foreach (var word in new[] { "Alpha", "Beta", "Beta", "Alpha" })
        {
            await page.On($"[data-ui-option]:has-text(\"{word}\")").Click();
            said.Add(page.TextOf("[data-ui-select-button]") + "/" + string.Join(',', answers.Many));
        }

        Assert.Equal(["Alpha/Alpha", "2 selected/Alpha,Beta", "Alpha/Alpha", "Choose…/"], said);
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    private sealed class Answers
    {
        public string Before { get; set; } = string.Empty;

        public int? After { get; set; }

        public string Text { get; set; } = string.Empty;

        public int? Number { get; set; }

        public bool? Agree { get; set; }

        public bool Alerts { get; set; }

        public string? One { get; set; }

        public List<string> Many { get; set; } = [];

        public DateOnly? Day { get; set; }

        public List<DateOnly> Days { get; set; } = [];

        public UiDateRange? Stay { get; set; }

        public TimeOnly? At { get; set; }

        public int Volume { get; set; }

        public int Stars { get; set; }

        public string? Body { get; set; }
    }
}
