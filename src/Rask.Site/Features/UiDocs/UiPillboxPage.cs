using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's pillbox and its parts as C# components: each example with its own code, and its reference.</summary>
[Route("pillbox")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiPillboxPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Pillbox.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Pillbox)
            .Lead("Several answers out of a list, each shown as a pill that can be taken off again, the box growing as "
                  + "they need. It holds a collection, and its options are its children.")
            .Api(UiApi.Pillbox)[
            UiDocsExample.Key("ui-pillbox").Heading("Pillbox").File("PillboxExample.cs").Demo(PillboxExample).Testid("ui-pillbox"),
            UiDocsExample.Key("ui-pillbox-sm").Heading("Small").File("PillboxSmallExample.cs").Demo(PillboxSmallExample).Testid("ui-pillbox-sm")
                .Blurb("A smaller pillbox for more compact layouts."),
            UiDocsExample.Key("ui-pillbox-searchable").Heading("Searchable").File("PillboxSearchableExample.cs").Demo(PillboxSearchableExample).Testid("ui-pillbox-searchable")
                .Blurb("A search field over the options, for a long list; SearchPlaceholder says what it says while it is empty."),
            UiDocsExample.Key("ui-pillbox-icons").Heading("With icons").File("PillboxIconsExample.cs").Demo(PillboxIconsExample).Testid("ui-pillbox-icons")
                .Blurb("An option's children are drawn as they are; its pill shows the words alone."),
            UiDocsExample.Key("ui-pillbox-combobox").Heading("Combobox").File("PillboxComboboxExample.cs").Demo(PillboxComboboxExample).Testid("ui-pillbox-combobox")
                .Blurb("An input among the pills: typing narrows the list, and Backspace in the empty input takes the last pill off."),
            UiDocsExample.Key("ui-pillbox-create").Heading("Create option").File("PillboxCreateExample.cs").Demo(PillboxCreateExample).Testid("ui-pillbox-create")
                .Blurb("Type a tag that is not there: from two characters on the create row offers to make it, and hides "
                       + "itself when the text names one that is."),
            UiDocsExample.Key("ui-pillbox-backend").Heading("With backend search").File("PillboxBackendSearchExample.cs").Demo(PillboxBackendSearchExample).Testid("ui-pillbox-backend")
                .Blurb("Filter(false) leaves the options to the page, asked once typing has paused. With the search "
                       + "cleared the query brings the picked tags back, so a tag just made is among them."),
            UiDocsExample.Key("ui-pillbox-store").Heading("Backend search in the search field").File("PillboxSearchStoreExample.cs").Demo(PillboxSearchStoreExample).Testid("ui-pillbox-store")
                .Blurb("The same over a searchable list, through Ui.PillboxSearch: the rows drawn stay until the answer is here."),
            UiDocsExample.Key("ui-pillbox-loading").Heading("Loading message").File("PillboxLoadingExample.cs").Demo(PillboxLoadingExample).Testid("ui-pillbox-loading")
                .Blurb("What the list says while a slow search is on its way, and when it finds nothing."),
            UiDocsExample.Key("ui-pillbox-validation").Heading("With validation").File("PillboxValidationExample.cs").Demo(PillboxValidationExample).Testid("ui-pillbox-validation")
                .Blurb("The page refuses a tag longer than twenty characters: the input is marked invalid until the next search."),
            UiDocsExample.Key("ui-pillbox-modal").Heading("With modal").File("PillboxModalExample.cs").Demo(PillboxModalExample).Testid("ui-pillbox-modal")
                .Blurb("With nothing to type into, the create row is always there, and opens a form of the page's own in a modal."),
            UiPillboxBehaviour
        ];
}
