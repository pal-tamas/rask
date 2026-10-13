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
        ["toggle checked"] = on => Ui.Toggle.Icon(Ui.IconName.Bolt).Checked(on),
        ["toggle checked with labels"] = on => Ui.Toggle.OnLabel("Sound on").OffLabel("Sound off").Checked(on),
        ["toggle disabled"] = on => Ui.Toggle.Icon(Ui.IconName.Bolt).Disabled(on),
        ["carousel disabled"] = on => Ui.Carousel.Disabled(on).Indicators()[Slides()],
        ["carousel indicators"] = on => Ui.Carousel.Indicators(on)[Slides()],
        ["carousel settings"] = on => Ui.Carousel.Autoplay(on).Wrap(on ? Ui.CarouselWrap.Rewind : null)[Slides()],
        ["field error before a description"] = on => Ui.Field[
            Ui.Label["Email"], Ui.Input.Value("a"), Ui.Error.Message(on ? "Enter an email address." : null), Ui.Description["Never shared."]
        ],
        ["field error last"] = on => Ui.Field[Ui.Label["Email"], Ui.Input.Value("a"), Ui.Error.Message(on ? "Enter an email address." : null)],
        ["navlist item badge"] = on => Ui.Navlist[Ui.NavlistItem.Href("/").Badge(on ? "3" : null)["Inbox"]],
        ["avatar badge"] = on => Ui.Avatar.Name("Ada Lovelace").Badge(on ? "3" : null),
        ["accordion item expanded"] = on => Ui.Accordion[Ui.AccordionItem.Expanded(on).Heading("Shipping")["Two days."]],
        ["date picker value"] = on => Ui.DatePicker.Bind(() => Stay.Of(on).Arrival).Locale("en-US"),
        ["time picker value"] = on => Ui.TimePicker.Value(on ? new TimeOnly(9, 30) : (TimeOnly?)null).Clearable(),
        ["color picker value"] = on => Ui.ColorPicker.Value(on ? "#ef4444" : string.Empty).Clearable().Copyable(),
        ["color picker value on a button"] = on => Ui.ColorPicker.Value(on ? "#ef4444" : string.Empty).Type(Ui.ColorPickerType.Button),
        ["color picker invalid"] = on => Ui.ColorPicker.Value("#ef4444").Invalid(on),
        ["color picker disabled"] = on => Ui.ColorPicker.Value("#ef4444").Disabled(on),
        ["select value"] = on => Ui.Select.Value(on ? "b" : null).Listbox.Clearable().Placeholder("Choose…")[Options()],
        ["select value in a combobox"] = on => Ui.Select.Value(on ? "b" : null).Combobox.Clearable().Placeholder("Choose…")[Options()],
        ["select value in a native select"] = on => Ui.Select.Value(on ? "b" : null).Placeholder("Choose…")[Options()],
        ["select options with and without an icon"] = on => Ui.Select.Value(on ? "a" : "b").Listbox[Options()],
        ["select options arriving"] = on => Ui.Select.Value((string?)null).Listbox.Searchable()[on ? Options() : []],
        ["select options leaving from under the answer"] = on => Ui.Select.Value("b").Listbox.Searchable().Filter(false)[on ? Options().Take(1) : Options()],
        ["pillbox values"] = on => Ui.Pillbox.Values(Picked(on)).Placeholder("Choose…")[Pills()],
        ["pillbox values with a clear button"] = on => Ui.Pillbox.Values(Picked(on))[Ui.PillboxTrigger.Placeholder("Choose…").Clearable(), Pills()],
        ["pillbox values in a combobox"] = on => Ui.Pillbox.Values(Picked(on)).Combobox.Placeholder("Choose…")[Pills()],
        ["autocomplete value"] = on => Ui.Autocomplete.Value(on ? "Alabama" : string.Empty).Clearable()[
            Ui.AutocompleteItem.Key("al")["Alabama"], Ui.AutocompleteItem.Key("ak")["Alaska"]
        ],
        ["progress value"] = on => Ui.Progress.Value(on ? 80 : 0),
        // An icon that goes in before words or content: one child arriving among siblings that still match,
        // which the differ answers with an insert of that child and nothing else.
        ["badge icon"] = on => Ui.Badge.Icon(on ? Ui.IconName.Check : null)["New"],
        ["callout icon"] = on => Ui.Callout.Icon(on ? Ui.IconName.Check : null).Heading("Saved"),
        ["navlist item icon"] = on => Ui.Navlist[Ui.NavlistItem.Href("/").Icon(on ? Ui.IconName.Check : null)["Inbox"]],
        // The keys beside a button's words: the words stay bare, as Flux leaves them, and the keys arrive after.
        ["button kbd"] = on => Ui.Button.Kbd(on ? "S" : null)["Save"],
    };

    /// <summary>
    ///     Prop changes the differ cannot follow node by node, and where each changes shape. Each puts one node
    ///     in the PLACE of another — bare words become a <c>&lt;span&gt;</c>, initials become an icon, a control
    ///     becomes a field around it — which is not a child that comes or goes: the differ cannot tell which
    ///     node stayed, so the parent it happens in is morphed. That is no longer the whole page, and each is
    ///     held here to the ONE parent it morphs — the component's own element, never the page around it — and
    ///     to no more characters of markup than it costs today. The fix is still a different shape, and for a Flux component
    ///     that is a parity decision, not a patch.
    /// </summary>
    private static readonly Dictionary<string, (Func<bool, Component> Draw, int Chars, string Where)> Morphs = new(StringComparer.Ordinal)
    {
        ["button icon"] = (on => Ui.Button.Icon(on ? Ui.IconName.Check : null)["Save"], 363,
            "src/Rask.Ui/UiButton.cs:316 — bare words become a <span> beside the icon"),
        ["button trailing icon"] = (on => Ui.Button.IconTrailing(on ? Ui.IconName.ChevronDown : null)["Save"], 354,
            "src/Rask.Ui/UiButton.cs:319 — bare words become a <span> beside the icon"),
        ["avatar icon for a name"] = (on => on ? Ui.Avatar.Icon(Ui.IconName.Check) : Ui.Avatar.Name("Ada Lovelace"), 359,
            "src/Rask.Ui/UiAvatar.cs:172 — an <svg> in place of the initials' <span>"),
        ["input label and description"] = (on => Ui.Input.Value("a").Label(on ? "Name" : null).Description(on ? "As on the card." : null), 1315,
            "src/Rask.Ui/UiWithField.cs:61 — a control with a label is wrapped in a field, one without is bare"),
        ["breadcrumbs item with and without a link"] = (on => on ? Ui.BreadcrumbsItem.Href("/orders")["Orders"] : Ui.BreadcrumbsItem["Orders"], 1009,
            "src/Rask.Ui/UiBreadcrumbsItem.cs:71 — the page being shown is a <div>, a step back to it is an <a>, as Flux draws them"),
    };

    public static TheoryData<string> Names => [.. Changes.Keys];

    public static TheoryData<string> MorphNames => [.. Morphs.Keys];

    private static List<string> Picked(bool on) => on ? ["Alpha", "Beta"] : [];

    private static IEnumerable<Component> Slides() => [Ui.CarouselSlide.Key("one")["One"], Ui.CarouselSlide.Key("two")["Two"]];

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
    [MemberData(nameof(MorphNames))]
    public void A_prop_change_that_puts_one_node_in_the_place_of_another_morphs_one_parent_within_its_bytes(string name)
    {
        var (draw, chars, where) = Morphs[name];
        var on = false;
        var page = Page.Render(() => Div[P["before"], draw(on), P["after"]]);

        on = true;
        page.Render();

        // Fixed? Then move it up into Changes, where it is held to a diff of the nodes that changed from now on.
        Assert.True(DiffGuardAttribute.Morphs().Count > 0, name + " is a diff of the nodes that changed now: " + where);
        var morph = Assert.Single(DiffGuardAttribute.Morphs());
        Assert.Empty(DiffGuardAttribute.FullPages());
        Assert.Equal("0/1", morph.Path);
        Assert.DoesNotContain("<p>before</p>", morph.Markup, StringComparison.Ordinal);
        Assert.InRange(morph.Markup.Length, 1, chars);
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
