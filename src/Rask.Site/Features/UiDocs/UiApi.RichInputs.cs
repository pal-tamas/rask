using System.Linq.Expressions;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs;

// The richer inputs' tables: autocomplete, pillbox, file upload, phone, editor, composer and toggle.
internal static partial class UiApi
{
    private const string Codes = "two-letter codes: [\"US\", \"CA\"]";

    private const string Pause = "a TimeSpan: 250.Milliseconds";

    public static IReadOnlyList<UiApiPart> Autocomplete { get; } =
    [
        new("Ui.Autocomplete", typeof(UiAutocomplete), "flux:autocomplete", "A text input that suggests what to type. Its items are its children.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The member it reads and writes: () => m.State. Or Value with OnChange.", "() => a string"),
            new("Value", typeof(string), "The text, for a page that holds it itself."),
            new("OnChange", typeof(Callback<string>), "Runs with the text when it is picked, cleared, or typed and left, and with Debounce once typing has paused.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the text is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as an item is picked, not with the next action."),
            new("Debounce", typeof(TimeSpan?), "Hands the page what was typed once typing has paused this long: suggestions from a database.", Pause),
            new("Type", typeof(InputType?), "The input's type. Text when unset."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the input."),
            new("Placeholder", typeof(string), "Shown while it is empty."),
            new("Size", typeof(Ui.AutocompleteSize?), "How tall it is. 40px when unset."),
            new("Variant", typeof(Ui.AutocompleteVariant?), "Outlined, or filled with no border. Outlined when unset."),
            new("Disabled", typeof(bool?), "An input that cannot be typed into or opened."),
            new("ReadOnly", typeof(bool?), "Shows its text and takes none."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Multiple", typeof(bool?), "For a file input: several files."),
            new("Mask", typeof(string), "A pattern the text is laid into: 99/99/9999."),
            new("Icon", typeof(UiInputIcon?), "An icon, or content, at the start of the input.", "Ui.IconName, or a component"),
            new("IconTrailing", typeof(UiInputIcon?), "An icon, or content, at the end of the input.", "Ui.IconName, or a component"),
            new("Kbd", typeof(string), "A keyboard shortcut shown at the end of the input."),
            new("Clearable", typeof(bool?), "A button that empties the input, while it has text."),
            new("Copyable", typeof(bool?), "A button that copies what it holds."),
            new("Viewable", typeof(bool?), "For a password input: a button that shows what was typed."),
            new("As", typeof(Ui.AutocompleteAs?), "What it is drawn as. The input when unset."),
            new("ContainerClass", typeof(string), "Classes for the open list: a height, such as max-h-80."),
            new("InputClass", typeof(string), "Classes for the <input> itself rather than the box around it."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.AutocompleteItem", typeof(UiAutocompleteItem), "flux:autocomplete.item", "One suggestion. Its children are its words.",
        [
            new("Disabled", typeof(bool?), "Shown but not pickable."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Pillbox { get; } =
    [
        new("Ui.Pillbox", typeof(UiPillbox<string>), "flux:pillbox", "Several answers as pills. Its options, and the slots below, are its children.",
        [
            new("Bind", typeof(Expression<Func<ICollection<string>>>), "The collection it reads and writes: () => m.Tags. Or Values with OnChange.", "() => a collection of T"),
            new("Value", typeof(ICollection<string>), "The answers, for a page that holds them itself: Values(tags).", "a collection of T"),
            new("OnChange", typeof(Callback<ICollection<string>>), "Runs with the new answers at each pick and each pill taken off.", Picked),
            new("AfterBind", typeof(Callback<ICollection<string>>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<ICollection<string>>?), "A rule the answers are checked by when they are written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as an answer is picked, not with the next action."),
            new("Variant", typeof(Ui.PillboxVariant?), "The pills alone, or an input among them. The pills when unset."),
            new("Placeholder", typeof(string), "Shown while nothing is picked."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the pillbox."),
            new("Size", typeof(Ui.PillboxSize?), "How tall it is while it holds one line of pills."),
            new("Searchable", typeof(bool?), "A search field over the options."),
            new("SearchPlaceholder", typeof(string), "What the search field says while it is empty. \"Search...\" when unset."),
            new("Filter", typeof(bool?), "False leaves every option in the list whatever is typed: the page filters."),
            new("Disabled", typeof(bool?), "A pillbox that cannot be opened or changed."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.PillboxOption", typeof(UiPillboxOption), "flux:pillbox.option", "One answer. Its children are its words, or content of your own.",
        [
            new("Value", typeof(object), "What picking it stores. Its own words when unset.", "a value of the pillbox's type"),
            new("Label", typeof(string), "Its words, in place of children."),
            new("SelectedLabel", typeof(string), "What its pill says, when that is not its words."),
            new("Disabled", typeof(bool?), "Shown but not pickable."),
            new("Filterable", typeof(bool?), "False for an option the search never hides."),
        ]),
        new("Ui.PillboxOptionCreate", typeof(UiPillboxOptionCreate), "flux:pillbox.option.create", "The row that makes a new option out of what was typed.",
        [
            new("MinLength", typeof(int?), "How long the typed text must be before the row is offered."),
            new("OnClick", typeof(Callback<string>), "Runs with the text as typed when the row is picked: Flux's wire:click.", "a handler given the text"),
        ]),
        new("Ui.PillboxOptionEmpty", typeof(UiPillboxOptionEmpty), "flux:pillbox.option.empty", "What the list says when nothing matches. Its children are its words.",
        [
            new("WhenLoading", typeof(string), "What it says while results are on their way. \"Loading...\" when unset."),
        ]),
        new("Ui.PillboxSearch", typeof(UiPillboxSearch), "flux:pillbox.search", "The search field over a searchable pillbox's list, as a child.",
        [
            new("Placeholder", typeof(string), "Shown while it is empty. \"Search...\" when unset."),
            new("Icon", typeof(Ui.IconName?), "The icon at its start. A magnifying glass when unset."),
            new("Clearable", typeof(bool?), "The button that empties it. Shown unless false."),
            new("Value", typeof(string), "The text, for a page that holds the search itself."),
            new("OnInput", typeof(Callback<string>), "Runs with the text as typed, or once typing has paused for Debounce.", Picked),
            new("Debounce", typeof(TimeSpan?), "How long typing pauses before OnInput runs.", Pause),
        ]),
        new("Ui.PillboxInput", typeof(UiPillboxInput), "flux:pillbox.input", "The input among a combobox pillbox's pills, as a child.",
        [
            new("Placeholder", typeof(string), "Shown while nothing is picked and nothing typed."),
            new("Invalid", typeof(bool?), "Error styling: a refused search."),
            new("Value", typeof(string), "The text, for a page that holds it itself: set it empty to clear the input."),
            new("OnInput", typeof(Callback<string>), "Runs with the text as typed, or once typing has paused for Debounce.", Picked),
            new("Debounce", typeof(TimeSpan?), "How long typing pauses before OnInput runs.", Pause),
        ]),
        new("Ui.PillboxTrigger", typeof(UiPillboxTrigger), "flux:pillbox.trigger", "The box the pills sit in, as a child, for what the pillbox's own steps do not reach.",
        [
            new("Placeholder", typeof(string), "Shown while nothing is picked."),
            new("Invalid", typeof(bool?), "Error styling."),
            new("Size", typeof(Ui.PillboxSize?), "How tall it is while it holds one line of pills."),
            new("Clearable", typeof(bool?), "A button that takes every pill off, while there is one."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> FileUpload { get; } =
    [
        new("Ui.FileUpload", typeof(UiFileUpload), "flux:file-upload",
            "The area that takes files. Its children are a dropzone, or markup of your own.",
        [
            new("OnFiles", typeof(Callback<IReadOnlyList<IRaskFile>>), "Runs with the files chosen or dropped, readable while it runs: Rask's stand-in for wire:model.", "a handler given the files"),
            new("Multiple", typeof(bool?), "Lets the reader choose, or drop, more than one file."),
            new("Accept", typeof(string), "The input's own accept list: \"image/*\". A filter on the picker and nothing more."),
            new("Label", typeof(string), "Draws a field around it, with this label."),
            new("Description", typeof(string), "Help text between the label and the upload."),
            new("Error", typeof(string), "A message under the upload, which also marks it invalid."),
            new("Name", typeof(string), "The input's name, for a form that is posted."),
            new("Disabled", typeof(bool?), "Takes no click, no key and no drop, and draws the dropzone muted."),
        ]),
        new("Ui.FileUploadDropzone", typeof(UiFileUploadDropzone), "flux:file-upload.dropzone", "The dashed area that says what to drop, and shows that an upload runs.",
        [
            new("Heading", typeof(string), "The line it leads with."),
            new("Text", typeof(string), "The smaller line under it: what is accepted, and how much."),
            new("Icon", typeof(Ui.IconName?), "The icon. A cloud with an arrow when unset."),
            new("Inline", typeof(bool?), "The compact layout: the icon beside the words."),
            new("WithProgress", typeof(bool?), "A bar that fills in place of Text while files upload, instead of a spinner."),
        ]),
        new("Ui.FileItem", typeof(UiFileItem), "flux:file-item", "One file as a row: a preview or an icon, its name, its size, and its actions.",
        [
            new("Heading", typeof(string), "The file's name, or another title for it."),
            new("Text", typeof(string), "The line under the heading. Written from Size when unset."),
            new("Image", typeof(string), "The address of a preview, shown in place of the icon."),
            new("Size", typeof(long?), "The file's size in bytes, shown as B, KB, MB or GB."),
            new("Icon", typeof(Ui.IconName?), "The icon shown when there is no image. A document when unset."),
            new("Invalid", typeof(bool?), "Draws it as a file that failed."),
            new("Actions", typeof(Component), "What sits at the end of the row: a Ui.FileItemRemove."),
        ]),
        new("Ui.FileItemRemove", typeof(UiFileItemRemove), "flux:file-item.remove", "The small cross for a file item's actions. It removes nothing itself.",
        [
            new("OnClick", typeof(Callback), "Runs when it is pressed: Rask's stand-in for wire:click.", Handler),
            new("AriaLabel", typeof(string), "Its accessible name. \"Remove file\" when unset; name the file in a list of several."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Phone { get; } =
    [
        new("Ui.Phone", typeof(UiPhone), "flux:phone", "An international phone number with a country list, bound as E.164 once it is a possible one.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The member it reads and writes: () => m.Mobile. Or Value with OnChange.", "() => a string"),
            new("Value", typeof(string), "The number as E.164, for a page that holds it itself."),
            new("OnChange", typeof(Callback<string>), "Runs with the number when it is committed.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the number is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as the number changes, after a 150 ms pause."),
            new("Debounce", typeof(TimeSpan?), "Writes the model once typing has paused this long.", "a TimeSpan: 300.Milliseconds"),
            new("Country", typeof(string), "The country of an empty number, by its code: \"HU\". None when unset."),
            new("Countries", typeof(IReadOnlyList<string>), "The countries the list offers. Every country when unset; what can be typed is not limited.", Codes),
            new("CountryOrder", typeof(IReadOnlyList<string>), "The countries the list shows first; the rest follow alphabetically.", Codes),
            new("Placeholder", typeof(string), "Shown while the number is empty."),
            new("Size", typeof(Ui.PhoneSize?), "How tall it is. 40px when unset."),
            new("Variant", typeof(Ui.PhoneVariant?), "Outlined, or filled. Outlined when unset."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the field."),
            new("DescriptionTrailing", typeof(string), "Help text under the field."),
            new("Name", typeof(string), "What the number is posted under from a plain form."),
            new("Disabled", typeof(bool?), "Takes no input, and the country cannot be changed."),
            new("ReadOnly", typeof(bool?), "Shows its number and takes the focus; the number cannot be edited."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Editor { get; } =
    [
        new("Ui.Editor", typeof(UiEditor), "flux:editor",
            "A toolbar over an editable area, whose value is HTML. Its children, when it has any, compose it from its parts.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The member the HTML is bound to: () => m.Body. Or Value with OnChange.", "() => a string"),
            new("Value", typeof(string), "The HTML it starts with, for a page that holds it itself."),
            new("OnChange", typeof(Callback<string>), "Runs with the HTML each time it changes; an empty document is \"\".", Picked),
            new("Live", typeof(bool?), "Writes the model at every change, not with the next action."),
            new("Toolbar", typeof(string), "The items, in order: \"heading | bold italic ~ undo redo\". | is a separator, ~ a spacer."),
            new("Placeholder", typeof(string), "Shown while the document is empty."),
            new("Label", typeof(string), "Draws a field around it, with this label."),
            new("Description", typeof(string), "Help text between the label and the editor."),
            new("DescriptionTrailing", typeof(string), "Help text under the editor."),
            new("Badge", typeof(string), "A word beside the label: \"Optional\"."),
            new("Disabled", typeof(bool?), "Read-only, with the toolbar off."),
            new("Invalid", typeof(bool?), "Error styling."),
        ]),
        new("Ui.EditorToolbar", typeof(UiEditorToolbar), "flux:editor.toolbar", "A composed editor's toolbar: the items named, or the ones inside it.",
        [
            new("Items", typeof(string), "The items, as the editor's Toolbar names them. With none, and nothing inside, the default toolbar."),
        ]),
        new("Ui.EditorContent", typeof(UiEditorContent), "flux:editor.content", "A composed editor's editable area. What is inside it is the HTML it starts with.", []),
        new("Ui.EditorButton", typeof(UiEditorButton), "flux:editor.button", "A toolbar button of your own: an icon, words, or both.",
        [
            new("Icon", typeof(Ui.IconName?), "The icon it shows."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini alone, micro beside words, when unset."),
            new("Tooltip", typeof(string), "A hint on hover, which also names a button with no words."),
            new("OnClick", typeof(Callback), "Runs when it is pressed.", Handler),
            new("Disabled", typeof(bool?), "A button that cannot be pressed."),
        ]),
        new("Ui.EditorHeading", typeof(UiEditorHeading), "flux:editor.heading", "Text, or a heading of level one to three, from a list.", []),
        new("Ui.EditorBold", typeof(UiEditorBold), "flux:editor.bold", "Bold.", []),
        new("Ui.EditorItalic", typeof(UiEditorItalic), "flux:editor.italic", "Italic.", []),
        new("Ui.EditorStrike", typeof(UiEditorStrike), "flux:editor.strike", "Strikethrough.", []),
        new("Ui.EditorUnderline", typeof(UiEditorUnderline), "flux:editor.underline", "Underline.", []),
        new("Ui.EditorSubscript", typeof(UiEditorSubscript), "flux:editor.subscript", "Subscript.", []),
        new("Ui.EditorSuperscript", typeof(UiEditorSuperscript), "flux:editor.superscript", "Superscript.", []),
        new("Ui.EditorHighlight", typeof(UiEditorHighlight), "flux:editor.highlight", "Highlighted text.", []),
        new("Ui.EditorCode", typeof(UiEditorCode), "flux:editor.code", "Inline code.", []),
        new("Ui.EditorBullet", typeof(UiEditorBullet), "flux:editor.bullet", "A bulleted list.", []),
        new("Ui.EditorOrdered", typeof(UiEditorOrdered), "flux:editor.ordered", "A numbered list.", []),
        new("Ui.EditorBlockquote", typeof(UiEditorBlockquote), "flux:editor.blockquote", "A block quote.", []),
        new("Ui.EditorLink", typeof(UiEditorLink), "flux:editor.link", "A panel with the address, a button to set it and one to remove the link.", []),
        new("Ui.EditorAlign", typeof(UiEditorAlign), "flux:editor.align", "Left, center or right, from a list.", []),
        new("Ui.EditorUndo", typeof(UiEditorUndo), "flux:editor.undo", "Undoes the last change.", []),
        new("Ui.EditorRedo", typeof(UiEditorRedo), "flux:editor.redo", "Redoes the last change undone.", []),
        new("Ui.EditorSeparator", typeof(UiEditorSeparator), "flux:editor.separator", "A hairline between two groups of items: | in a list.", []),
        new("Ui.EditorSpacer", typeof(UiEditorSpacer), "flux:editor.spacer", "Pushes the items after it to the far edge: ~ in a list.", []),
    ];

    public static IReadOnlyList<UiApiPart> Composer { get; } =
    [
        new("Ui.Composer", typeof(UiComposer<string>), "flux:composer", "A message box with buttons around it, which sends the form it is in from the keyboard.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The member it reads and writes: () => m.Prompt. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(string), "The text, for a page that holds it itself.", "T, the type it edits"),
            new("OnChange", typeof(Callback<string>), "Runs with the text when it is committed.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the text is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as it is typed, after a 150 ms pause. Without it the text travels with the submit."),
            new("Debounce", typeof(TimeSpan?), "Writes the model once typing has paused this long.", "a TimeSpan: 300.Milliseconds"),
            new("Submit", typeof(Ui.ComposerSubmit?), "Which key sends the form: Ctrl/Cmd + Enter when unset, or Enter (.Enter)."),
            new("Rows", typeof(int?), "How many lines the box shows to start with. Two when unset."),
            new("MaxRows", typeof(int?), "How many lines it grows to before it scrolls. Ten when unset."),
            new("Inline", typeof(bool?), "The buttons in one row with the box, the leading ones before it."),
            new("Variant", typeof(Ui.ComposerVariant?), "The corners: a message box's when unset, or those of the other inputs."),
            new("Placeholder", typeof(string), "Shown while the box is empty."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("LabelSrOnly", typeof(bool?), "Keeps the label for screen readers only."),
            new("Description", typeof(string), "Help text in the field around it."),
            new("DescriptionSrOnly", typeof(bool?), "Keeps the description for screen readers only."),
            new("Input", typeof(Component), "What stands where the box is: a Ui.Editor for rich text."),
            new("Header", typeof(Component), "What sits above the box: the files about to be sent."),
            new("Footer", typeof(Component), "What sits under the buttons."),
            new("ActionsLeading", typeof(Component), "The buttons at the start of the row under the box."),
            new("ActionsTrailing", typeof(Component), "The buttons at its end, the submit button among them."),
            new("Name", typeof(string), "What the text is posted under from a plain form."),
            new("Disabled", typeof(bool?), "Locks the box and every button in it."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Toggle { get; } =
    [
        new("Ui.Toggle", typeof(UiToggle), "flux:toggle", "A setting turned on or off from a compact button. Its children, when it has any, are its label.",
        [
            new("Bind", typeof(Expression<Func<bool>>), "The bool it reads and writes: () => m.FastMode.", "() => a bool"),
            new("OnChange", typeof(Callback<bool>), "Runs with the new state at each turn, after the write when it is bound: Flux's wire:change.", Toggled),
            new("Live", typeof(bool?), "Writes the model as soon as it is turned, not with the next action."),
            new("Checked", typeof(bool?), "Whether it is on, for a toggle that is not bound: where it starts."),
            new("Icon", typeof(UiInputIcon?), "A Heroicon, outlined when off and solid when on, or icons of your own.", "Ui.IconName, or a component"),
            new("OnIcon", typeof(Ui.IconName?), "The icon shown when on, in place of Icon."),
            new("OffIcon", typeof(Ui.IconName?), "The icon shown when off, in place of Icon."),
            new("Label", typeof(string), "Words beside the icon, in both states."),
            new("OnLabel", typeof(string), "The words shown when on."),
            new("OffLabel", typeof(string), "The words shown when off."),
            new("Size", typeof(Ui.ToggleSize?), "How large it is. Base, 40px, when unset."),
            new("Variant", typeof(Ui.ToggleVariant?), "How it is drawn. Outline when unset."),
            new("Color", typeof(Ui.Color?), "The icon's colour when on. The accent when unset.", Hue),
            new("Tooltip", typeof(string), "A hint on hover, which also names a toggle that shows only an icon."),
            new("Disabled", typeof(bool?), "Dimmed, and out of the tab order."),
        ]),
    ];
}
