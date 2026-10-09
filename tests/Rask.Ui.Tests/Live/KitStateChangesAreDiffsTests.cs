using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A kit component drawn again with one prop turned on, and off again: each change is one the live diff ships.
/// </summary>
/// <remarks>
///     <see cref="DiffGuardAttribute" /> holds every test in this assembly to that, so whatever a component test
///     drives — a pick, a key, a toggle — is covered where it is written. What those tests seldom do is change
///     a PROP between two renders, which is what a page does when its own state changes: a button that starts
///     loading, a field that gains an error, a row that gains a badge. Those are here, each between two siblings,
///     as a page would have them.
/// </remarks>
public partial class KitStateChangesAreDiffsTests : global::Rask.Core.RaskMarkup
{
    private static readonly DateOnly Day = new(2026, 10, 9);

    private static readonly Dictionary<string, Func<bool, Component>> Changes = new(StringComparer.Ordinal)
    {
        ["button loading"] = on => Ui.Button.Loading(on)["Save"],
        ["button disabled"] = on => Ui.Button.Disabled(on)["Save"],
        ["callout text"] = on => Ui.Callout.Heading("Saved").Text(on ? "Nothing else to do." : null),
        ["input invalid"] = on => Ui.Input.Value("a").Invalid(on),
        ["input clearable"] = on => Ui.Input.Value("a").Clearable(on),
        ["input copyable"] = on => Ui.Input.Value("a").Copyable(on),
        ["input viewable"] = on => Ui.Input.Value("a").Viewable(on),
        ["input kbd"] = on => Ui.Input.Value("a").Kbd(on ? "K" : null),
        ["input value"] = on => Ui.Input.Value(on ? "a" : string.Empty).Clearable(),
        ["textarea invalid"] = on => Ui.Textarea.Value("a").Invalid(on),
        ["checkbox checked"] = on => Ui.Checkbox.Value(on).Label("Agree"),
        ["checkbox invalid"] = on => Ui.Checkbox.Value(false).Invalid(on),
        ["switch on"] = on => Ui.Switch.Value(on).Label("Alerts"),
        ["field error before a description"] = on => Ui.Field[
            Ui.Label["Email"], Ui.Input.Value("a"), Ui.Error.Message(on ? "Enter an email address." : null), Ui.Description["Never shared."]
        ],
        ["field error last"] = on => Ui.Field[Ui.Label["Email"], Ui.Input.Value("a"), Ui.Error.Message(on ? "Enter an email address." : null)],
        ["navlist item badge"] = on => Ui.Navlist[Ui.NavlistItem.Href("/").Badge(on ? "3" : null)["Inbox"]],
        ["avatar badge"] = on => Ui.Avatar.Name("Ada Lovelace").Badge(on ? "3" : null),
        ["accordion item expanded"] = on => Ui.Accordion[Ui.AccordionItem.Expanded(on).Heading("Shipping")["Two days."]],
        ["date picker value"] = on => Ui.DatePicker.Bind(() => Stay.Of(on).Arrival).Locale("en-US"),
        ["time picker value"] = on => Ui.TimePicker.Value(on ? new TimeOnly(9, 30) : (TimeOnly?)null).Clearable(),
        ["select value"] = on => Ui.Select.Value(on ? "b" : null).Listbox.Clearable().Placeholder("Choose…")[Options()],
        ["select value in a combobox"] = on => Ui.Select.Value(on ? "b" : null).Combobox.Clearable().Placeholder("Choose…")[Options()],
        ["select value in a native select"] = on => Ui.Select.Value(on ? "b" : null).Placeholder("Choose…")[Options()],
        ["select options with and without an icon"] = on => Ui.Select.Value(on ? "a" : "b").Listbox[Options()],
        ["select options arriving"] = on => Ui.Select.Value((string?)null).Listbox.Searchable()[on ? Options() : []],
        ["pillbox values"] = on => Ui.Pillbox.Values(Picked(on)).Placeholder("Choose…")[Pills()],
        ["pillbox values with a clear button"] = on => Ui.Pillbox.Values(Picked(on))[Ui.PillboxTrigger.Placeholder("Choose…").Clearable(), Pills()],
        ["pillbox values in a combobox"] = on => Ui.Pillbox.Values(Picked(on)).Combobox.Placeholder("Choose…")[Pills()],
        ["autocomplete value"] = on => Ui.Autocomplete.Value(on ? "Alabama" : string.Empty).Clearable()[
            Ui.AutocompleteItem.Key("al")["Alabama"], Ui.AutocompleteItem.Key("ak")["Alaska"]
        ],
        ["progress value"] = on => Ui.Progress.Value(on ? 80 : 0),
    };

    /// <summary>
    ///     Prop changes the gate still answers with the whole page, and where each changes shape. Each is a node
    ///     that goes in BEFORE content the page wrote, which is text and cannot carry a key — so the fix is a
    ///     different shape, and for a Flux component that is a parity decision, not a patch.
    /// </summary>
    private static readonly Dictionary<string, (Func<bool, Component> Draw, string Where)> Refused = new(StringComparer.Ordinal)
    {
        ["button icon"] = (on => Ui.Button.Icon(on ? Ui.IconName.Check : null)["Save"],
            "src/Rask.Ui/UiButton.cs:316 — bare words become a <span> beside the icon"),
        ["button trailing icon"] = (on => Ui.Button.IconTrailing(on ? Ui.IconName.ChevronDown : null)["Save"],
            "src/Rask.Ui/UiButton.cs:319 — bare words become a <span> beside the icon"),
        ["button kbd"] = (on => Ui.Button.Kbd(on ? "S" : null)["Save"],
            "src/Rask.Ui/UiButton.cs:318 — bare words become a <span> beside the keys"),
        ["badge icon"] = (on => Ui.Badge.Icon(on ? Ui.IconName.Check : null)["New"],
            "src/Rask.Ui/UiBadge.cs:100 — the icon goes in before the words, which are text"),
        ["callout icon"] = (on => Ui.Callout.Icon(on ? Ui.IconName.Check : null).Heading("Saved"),
            "src/Rask.Ui/UiCallout.cs:133 — the icon goes in before the content"),
        ["navlist item icon"] = (on => Ui.Navlist[Ui.NavlistItem.Href("/").Icon(on ? Ui.IconName.Check : null)["Inbox"]],
            "src/Rask.Ui/UiNavlistItem.cs:60 — the icon goes in before the words"),
        ["avatar icon for a name"] = (on => on ? Ui.Avatar.Icon(Ui.IconName.Check) : Ui.Avatar.Name("Ada Lovelace"),
            "src/Rask.Ui/UiAvatar.cs:172 — an <svg> in place of the initials' <span>"),
        ["input label and description"] = (on => Ui.Input.Value("a").Label(on ? "Name" : null).Description(on ? "As on the card." : null),
            "src/Rask.Ui/UiWithField.cs:61 — a control with a label is wrapped in a field, one without is bare"),
    };

    public static TheoryData<string> Names => [.. Changes.Keys];

    public static TheoryData<string> RefusedNames => [.. Refused.Keys];

    private static List<string> Picked(bool on) => on ? ["Alpha", "Beta"] : [];

    private static IEnumerable<Component> Pills() => [Ui.PillboxOption["Alpha"], Ui.PillboxOption["Beta"]];

    private static IEnumerable<Component> Options() =>
    [
        Ui.SelectOption.Key("a").Value("a").Icon(Ui.IconName.Check)["Alpha"],
        Ui.SelectOption.Key("b").Value("b")["Beta"],
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void Turning_a_prop_on_and_off_again_is_answered_with_a_diff_each_time(string name)
    {
        var on = false;
        var page = Page.Render(() => Div[P["before"], Changes[name](on), P["after"]]);

        on = true;
        page.Render();
        on = false;
        page.Render();

        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public void A_prop_change_the_gate_still_refuses_is_listed_until_it_is_fixed(string name)
    {
        var on = false;
        var page = Page.Render(() => Div[P["before"], Refused[name].Draw(on), P["after"]]);

        on = true;
        page.Render();

        // Fixed? Then move it up into Changes, where it is held to a diff from now on.
        Assert.True(DiffGuardAttribute.FullPages().Count > 0, name + " is a diff now: " + Refused[name].Where);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_toast_arriving_replaced_and_leaving_is_a_diff_wherever_its_outlet_is_placed(bool last, bool grouped)
    {
        var toasts = new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider();
        Component Outlet() => grouped ? Ui.ToastGroup[Ui.Toast] : Ui.Toast;
        var page = Page.Render(
            () => Div[Button.OnClick(() => Toast.Error("That name is taken."))["Save"], last ? null : Outlet(), Footer["after"], last ? Outlet() : null],
            toasts);

        await page.Click("Save");
        await page.Click("Save");
        var shown = page.FindAll("[data-ui-toast-dialog]").Count;
        while (page.FindAll("[data-rask-dismiss]") is [var close, ..])
        {
            await page.Invoke(close.Attribute("data-rask-on-click")!);
        }

        Assert.Equal(grouped ? 2 : 1, shown);
        Assert.False(page.Exists("[data-ui-toast-dialog]"));
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    private sealed class Stay
    {
        public DateOnly? Arrival { get; set; }

        internal static Stay Of(bool arrived) => new() { Arrival = arrived ? Day : null };
    }
}
