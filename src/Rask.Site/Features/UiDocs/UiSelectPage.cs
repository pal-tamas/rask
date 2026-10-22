using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's select and its parts as C# components: each example with its own code, and its reference.</summary>
[Route("select")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSelectPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Select.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Select)
            .Lead("One option from a list, or several. The browser's own select, a listbox the kit draws for options "
                  + "that carry more than words, or a combobox that filters as it is typed into; the options are its "
                  + "children, typed by the select's value.")
            .Api(UiApi.Select)[
            Picking(),
            Searching(),
            Combobox(),
            UiSelectBehaviour
        ];

    private static Component[] Picking() =>
    [
        UiDocsExample.Key("ui-select").Heading("Select").File("SelectExample.cs").Demo(SelectExample).Testid("ui-select")
            .Blurb("The browser's own select, in the input's box. For a list of up to five, radios or checkboxes may serve better."),
        UiDocsExample.Key("ui-select-sm").Heading("Small").File("SelectSmallExample.cs").Demo(SelectSmallExample).Testid("ui-select-sm"),
        UiDocsExample.Key("ui-select-groups").Heading("Option groups").File("SelectGroupsExample.cs").Demo(SelectGroupsExample).Testid("ui-select-groups")
            .Blurb("Related options under a label, to make a long list easier to scan."),
        UiDocsExample.Key("ui-listbox").Heading("Custom select").File("SelectListboxExample.cs").Demo(SelectListboxExample).Testid("ui-listbox")
            .Blurb("Listbox draws the list instead of handing it to the browser, for options with icons, pictures and more."),
        UiDocsExample.Key("ui-select-button").Heading("The button slot").File("SelectButtonSlotExample.cs").Demo(SelectButtonSlotExample).Testid("ui-select-button")
            .Blurb("Ui.SelectButton as a child sets what the select's own steps do not reach."),
        UiDocsExample.Key("ui-select-clearable").Heading("Clearable").File("SelectClearableExample.cs").Demo(SelectClearableExample).Testid("ui-select-clearable")
            .Blurb("A button that puts the select back to nothing chosen, shown while there is an answer."),
        UiDocsExample.Key("ui-select-prefix").Heading("With a prefix").File("SelectPrefixExample.cs").Demo(SelectPrefixExample).Testid("ui-select-prefix")
            .Blurb("Words that stay in the button before the chosen option."),
        UiDocsExample.Key("ui-select-icons").Heading("With icons").File("SelectIconsExample.cs").Demo(SelectIconsExample).Testid("ui-select-icons"),
        UiDocsExample.Key("ui-select-descriptions").Heading("With descriptions").File("SelectDescriptionsExample.cs").Demo(SelectDescriptionsExample).Testid("ui-select-descriptions")
            .Blurb("A description is the list's only: the button does not repeat it."),
        UiDocsExample.Key("ui-select-avatars").Heading("With avatars").File("SelectAvatarsExample.cs").Demo(SelectAvatarsExample).Testid("ui-select-avatars"),
        UiDocsExample.Key("ui-select-content").Heading("With custom content").File("SelectCustomContentExample.cs").Demo(SelectCustomContentExample).Testid("ui-select-content")
            .Blurb("An option's children are drawn as they are."),
        UiDocsExample.Key("ui-select-width").Heading("Customizing the dropdown width").File("SelectWidthExample.cs").Demo(SelectWidthExample).Testid("ui-select-width")
            .Blurb("The list is as wide as its button; OptionsClass widens it."),
    ];

    private static Component[] Searching() =>
    [
        UiDocsExample.Key("ui-select-search").Heading("Searchable select").File("SelectSearchableExample.cs").Demo(SelectSearchableExample).Testid("ui-select-search")
            .Blurb("A search field over the options, for a long list."),
        UiDocsExample.Key("ui-select-search-slot").Heading("The search slot").File("SelectSearchSlotExample.cs").Demo(SelectSearchSlotExample).Testid("ui-select-search-slot")
            .Blurb("Ui.SelectSearch as a child sets the search field's own steps."),
        UiDocsExample.Key("ui-select-keywords").Heading("Search keywords").File("SelectKeywordsExample.cs").Demo(SelectKeywordsExample).Testid("ui-select-keywords")
            .Blurb("Search for \"apple\", \"orange\" or \"pear\": Fruit is found by words it never shows."),
        UiDocsExample.Key("ui-multiselect").Heading("Multiple select").File("SelectMultipleExample.cs").Demo(SelectMultipleExample).Testid("ui-multiselect")
            .Blurb("Opened on a collection, the listbox picks several and stays open while you do."),
        UiDocsExample.Key("ui-select-suffix").Heading("Selected suffix").File("SelectSelectedSuffixExample.cs").Demo(SelectSelectedSuffixExample).Testid("ui-select-suffix")
            .Blurb("What follows the count once several are picked: \"2 industries selected\"."),
        UiDocsExample.Key("ui-select-clear").Heading("Clearing search").File("SelectClearSearchExample.cs").Demo(SelectClearSearchExample).Testid("ui-select-clear")
            .Blurb("Clear(Ui.SelectClear.Close) keeps the search through each pick and empties it when the list closes."),
    ];

    private static Component[] Combobox() =>
    [
        UiDocsExample.Key("ui-combobox").Heading("Combobox").File("SelectComboboxExample.cs").Demo(SelectComboboxExample).Testid("ui-combobox")
            .Blurb("A text input that filters the list under it as you type."),
        UiDocsExample.Key("ui-select-input").Heading("The input slot").File("SelectInputSlotExample.cs").Demo(SelectInputSlotExample).Testid("ui-select-input")
            .Blurb("Ui.SelectInput as a child sets the input's own steps, and can hand its text to the page."),
        UiDocsExample.Key("ui-select-backend").Heading("Backend search").File("SelectBackendSearchExample.cs").Demo(SelectBackendSearchExample).Testid("ui-select-backend")
            .Blurb("Filter(false) leaves the rows to the page, and Debounce asks it once typing has paused: one query "
                   + "for a word, not one for a letter."),
        UiDocsExample.Key("ui-select-store").Heading("Backend search in a listbox").File("SelectListboxSearchExample.cs").Demo(SelectListboxSearchExample).Testid("ui-select-store")
            .Blurb("The same in a searchable listbox, through Ui.SelectSearch. The rows drawn stay until the answer is "
                   + "here, and a row picked while the pause runs is the row you pressed."),
        UiDocsExample.Key("ui-select-create").Heading("Create option").File("SelectCreateExample.cs").Demo(SelectCreateExample).Testid("ui-select-create")
            .Blurb("Type a name no project has: the create row offers to make it, from two characters on."),
        UiDocsExample.Key("ui-select-create-search").Heading("With backend search").File("SelectCreateSearchExample.cs").Demo(SelectCreateSearchExample).Testid("ui-select-create-search")
            .Blurb("When the page filters, keep the chosen project among the rows, so the select can go on naming it."),
        UiDocsExample.Key("ui-select-loading").Heading("Loading message").File("SelectLoadingExample.cs").Demo(SelectLoadingExample).Testid("ui-select-loading")
            .Blurb("What the list says while a slow search is on its way, and when it finds nothing."),
        UiDocsExample.Key("ui-select-validation").Heading("With validation").File("SelectValidationExample.cs").Demo(SelectValidationExample).Testid("ui-select-validation")
            .Blurb("The page refuses a name longer than forty characters: the input is marked invalid until the next search."),
        UiDocsExample.Key("ui-select-modal").Heading("With modal").File("SelectModalExample.cs").Demo(SelectModalExample).Testid("ui-select-modal")
            .Blurb("A create row in a listbox opens a form of the page's own in a modal."),
    ];
}

