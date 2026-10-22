using System.Linq.Expressions;
using Rask.Core;
using Rask.Core.Forms;

namespace Rask.Site.Features.UiDocs;

// The form controls' tables: field, input, textarea, select, checkbox, radio, switch, slider and the OTP input.
internal static partial class UiApi
{
    private const string Validates = "a validator";

    private const string Attrs = "(\"aria-label\", \"Search\") pairs";

    public static IReadOnlyList<UiApiPart> Field { get; } =
    [
        new("Ui.Field", typeof(UiField), "flux:field", "A label, a control, help text and the control's message, stacked and wired together.",
        [
            new("Variant", typeof(Ui.FieldVariant?), "Inline puts the label beside the control, for a checkbox or a switch. Stacked when unset."),
        ]),
        new("Ui.Label", typeof(UiLabel), "flux:label", "A <label> for the field's control. Its children are its words.",
        [
            new("Badge", typeof(string), "A word beside the label: \"Required\", \"Optional\"."),
            new("Trailing", typeof(Component), "Content at the far end of the label's row: the slider's value."),
            new("For", typeof(string), "The id of the control it names, for a label outside a field."),
        ]),
        new("Ui.Description", typeof(UiDescription), "flux:description", "Help text: under the label before the control, under the control after it.", []),
        new("Ui.Error", typeof(UiError), "flux:error", "The first message the form holds for the field's control, in a live region.",
        [
            new("For", typeof(LambdaExpression), "The member whose message it shows, anywhere in a form: () => m.Email.", "() => a member"),
            new("Name", typeof(string), "The name of a member of the form's model, in place of For."),
            new("Message", typeof(string), "Words of your own, whatever the form says. Nothing is shown while it is null."),
            new("Icon", typeof(UiErrorIcon?), "The icon before the message, or false for none. A triangle when unset.", "a Ui.IconName, or false"),
        ]),
        new("Ui.Fieldset", typeof(UiFieldset), "flux:fieldset", "A <fieldset> around related fields.",
        [
            new("Legend", typeof(string), "The fieldset's heading, or place a Ui.Legend yourself."),
            new("Description", typeof(string), "Help text under the legend."),
        ]),
        new("Ui.Legend", typeof(UiLegend), "flux:legend", "A fieldset's heading. Its children are its words.", []),
    ];

    public static IReadOnlyList<UiApiPart> Input { get; } =
    [
        new("Ui.Input", typeof(UiInput<string>), "flux:input", "A line of text in a box that holds its icons and buttons. Its type is the model's.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The model's member it reads and writes: () => m.Email. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(string), "What it holds, for a page that holds it itself.", "T, the type it edits"),
            new("OnChange", typeof(Callback<string>), "Runs with the new value when it is committed.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as it is typed, after a 150 ms pause: Flux's wire:model.live."),
            new("Blur", typeof(bool?), "Writes the model when the reader leaves the field: Flux's wire:model.blur."),
            new("Debounce", typeof(TimeSpan?), "Writes the model once typing has paused this long.", "a TimeSpan: 300.Milliseconds"),
            new("OnInput", typeof(Callback<string>), "Runs at every keystroke with the text as typed.", Picked),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the input."),
            new("DescriptionTrailing", typeof(string), "Help text under the input."),
            new("Placeholder", typeof(string), "Shown while it is empty."),
            new("Type", typeof(InputType?), "The input's type: Email, Password, Date, File… The model's type decides when unset."),
            new("Size", typeof(Ui.InputSize?), "How tall it is. 40px when unset."),
            new("Variant", typeof(Ui.InputVariant?), "How it is drawn. Outlined when unset."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
            new("ReadOnly", typeof(bool?), "May be read and copied, not changed."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for. A bound input is invalid while its member holds a message."),
            new("Mask", typeof(string), "Holds each keystroke to a pattern: 9 a digit, a a letter, * either."),
            new("Money", typeof(string), "Groups an amount as it is typed, 1,234.56, and reads it back with the same marks: Flux's $money($input).",
                "decimals: \".\", thousands: \",\", precision: 2"),
            new("Icon", typeof(UiInputIcon?), "An icon, or content of your own, at the start of the box.", "a Ui.IconName, or a component"),
            new("IconTrailing", typeof(UiInputIcon?), "An icon, or content of your own such as a button, at the end.", "a Ui.IconName, or a component"),
            new("Kbd", typeof(string), "A keyboard shortcut shown at the end: \"⌘K\"."),
            new("Clearable", typeof(bool?), "A button that empties it, shown while there is text."),
            new("Copyable", typeof(bool?), "A button that copies the value."),
            new("Viewable", typeof(bool?), "A button that shows a password and hides it again."),
            new("As", typeof(Ui.InputAs?), "Button draws a <button> as the input, pressed with OnClick."),
            new("OnClick", typeof(Callback), "Runs when an input drawn as a button is pressed.", Handler),
            new("Multiple", typeof(bool?), "Lets a file input take several files."),
            new("OnFiles", typeof(Callback<IReadOnlyList<IRaskFile>>), "Runs with the files chosen in a file input.", "a handler given the files"),
            new("InputClass", typeof(string), "Classes for the <input> itself; Class goes on the box around it."),
            new("Min", typeof(string), "The input's own min: the lowest number or the first day."),
            new("Max", typeof(string), "The input's own max."),
            new("Step", typeof(string), "The input's own step."),
            new("MaxLength", typeof(int?), "The most characters it takes."),
            new("Autofocus", typeof(bool?), "Takes the focus when the page opens."),
            new("Name", typeof(string), "What it is posted under in a form."),
            new("Ref", typeof(ElementRef), "A reference to the <input>, for a call through the browser's APIs."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
            new("Attributes", typeof(IReadOnlyDictionary<string, string?>), "Attributes for the <input> itself.", Attrs),
        ]),
        new("Ui.InputGroup", typeof(UiInputGroup), "flux:input.group", "An input and its neighbours in one outline. In a field, it stands for its input.", []),
        new("Ui.InputGroupPrefix", typeof(UiInputGroupPrefix), "flux:input.group.prefix", "Words before the input: \"https://\".",
        [
            new("Size", typeof(Ui.InputSize?), "How tall it is, to match a smaller input."),
        ]),
        new("Ui.InputGroupSuffix", typeof(UiInputGroupSuffix), "flux:input.group.suffix", "Words after the input: \".brand.com\".",
        [
            new("Size", typeof(Ui.InputSize?), "How tall it is, to match a smaller input."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Textarea { get; } =
    [
        new("Ui.Textarea", typeof(UiTextarea<string>), "flux:textarea", "Several lines of text.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The model's member it reads and writes: () => m.Notes. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(string), "What it holds, for a page that holds it itself.", "T, the type it edits"),
            new("OnChange", typeof(Callback<string>), "Runs with the new text when it is committed.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as it is typed, after a pause: Flux's wire:model.live."),
            new("Blur", typeof(bool?), "Writes the model when the reader leaves the field."),
            new("Debounce", typeof(TimeSpan?), "Writes the model once typing has paused this long.", "a TimeSpan: 300.Milliseconds"),
            new("OnInput", typeof(Callback<string>), "Runs at every keystroke with the text as typed.", Picked),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the textarea."),
            new("DescriptionTrailing", typeof(string), "Help text under the textarea."),
            new("Badge", typeof(string), "A word beside the label: \"Optional\"."),
            new("Placeholder", typeof(string), "Shown while it is empty."),
            new("Rows", typeof(UiTextareaRows?), "How many lines tall it is, or UiTextareaRows.Auto to grow with the text. Four when unset.", "an int, or UiTextareaRows.Auto"),
            new("Resize", typeof(Ui.TextareaResize?), "Which way the reader may drag it. Down when unset."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
            new("ReadOnly", typeof(bool?), "May be read and copied, not changed."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Select { get; } =
    [
        new("Ui.Select", typeof(UiSelect<string>), "flux:select", "One option from a list, or several when it is opened on a collection. Its options are its children.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The model's member it reads and writes: () => m.Country, or a list for several. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(string), "The chosen option's value, for a page that holds it itself. Values(list) for several.", "T, the options' type"),
            new("OnChange", typeof(Callback<string>), "Runs with the new value when an option is picked.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as an option is picked, not with the next action."),
            new("Variant", typeof(Ui.SelectVariant?), "The browser's own select, a drawn listbox, or a combobox. The browser's when unset."),
            new("Size", typeof(Ui.SelectSize?), "How tall it is. 40px when unset."),
            new("Placeholder", typeof(string), "Shown while nothing is picked."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the select."),
            new("DescriptionTrailing", typeof(string), "Help text under the select."),
            new("Badge", typeof(string), "A word beside the label: \"Optional\"."),
            new("Multiple", typeof(bool?), "Picks several, over a collection. The listbox's."),
            new("Indicator", typeof(Ui.SelectIndicator?), "A checkmark beside the picked rows, or a checkbox beside every row. The listbox's; a checkmark when unset."),
            new("Searchable", typeof(bool?), "A search field over the listbox's options."),
            new("Filter", typeof(bool?), "False leaves every option in the list whatever is typed: the page filters, from the search's OnInput."),
            new("Empty", typeof(string), "What a search that matches nothing says. \"No results found\" when unset."),
            new("Clearable", typeof(bool?), "A button that clears the answer, shown while there is one."),
            new("Prefix", typeof(string), "Words that stay in the button before the chosen option: \"Compare to\"."),
            new("SelectedSuffix", typeof(string), "What follows the count once several are picked. \"selected\" when unset."),
            new("Clear", typeof(Ui.SelectClear?), "When a search empties itself: at every pick, or when the list closes."),
            new("Position", typeof(Ui.Position?), "Which side of the select the list opens on. Below when unset."),
            new("Align", typeof(Ui.Align?), "Which edge of the select the list lines up with. Its start when unset."),
            new("OptionsClass", typeof(string), "Classes for the open list: a width, such as min-w-72."),
            new("Disabled", typeof(bool?), "A select that cannot be opened or changed."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Name", typeof(string), "What the answer is posted under in a form."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.SelectOption", typeof(UiSelectOption), "flux:select.option", "One answer. Its children are its words, or content of your own.",
        [
            new("Value", typeof(object), "What the select holds while it is chosen. Its own words when unset.", "a value of the select's type"),
            new("Label", typeof(string), "Its words, in place of children."),
            new("SelectedLabel", typeof(string), "What the button says while it is chosen, when that is not its own words."),
            new("Keywords", typeof(string), "More words a search finds it by, never shown: \"apple orange pear\"."),
            new("Icon", typeof(Ui.IconName?), "An icon at its start, in a listbox or a combobox."),
            new("IconVariant", typeof(Ui.IconVariant?), "Which drawing of the icon. Mini when unset."),
            new("IconClass", typeof(string), "Classes for the icon."),
            new("Avatar", typeof(string), "A picture's address, drawn as an avatar at its start. It wins over an icon."),
            new("Description", typeof(string), "Words under its label in the list, not in the button."),
            new("Disabled", typeof(bool?), "Shown but not pickable."),
        ]),
        new("Ui.SelectGroup", typeof(UiSelectGroup), "flux:select.group", "Options under a heading: an <optgroup> in the browser's select.",
        [
            new("Label", typeof(string), "The heading over its options."),
        ]),
        new("Ui.SelectButton", typeof(UiSelectButton), "flux:select.button", "The listbox's button, as a child, for what the select's steps do not reach.",
        [
            new("Placeholder", typeof(string), "Shown while nothing is picked."),
            new("Invalid", typeof(bool?), "Error styling."),
            new("Size", typeof(Ui.SelectSize?), "How tall it is."),
            new("Disabled", typeof(bool?), "A button that cannot be pressed."),
            new("Clearable", typeof(bool?), "A button that clears the answer."),
        ]),
        new("Ui.SelectInput", typeof(UiSelectInput), "flux:select.input", "The combobox's input, as a child, and what is typed into it.",
        [
            new("Placeholder", typeof(string), "Shown while it is empty."),
            new("Invalid", typeof(bool?), "Error styling: a refused search."),
            new("Size", typeof(Ui.SelectSize?), "How tall it is."),
            new("Value", typeof(string), "The text, for a page that holds the search itself."),
            new("OnInput", typeof(Callback<string>), "Runs with the text as typed, or once typing has paused for Debounce.", Picked),
            new("Debounce", typeof(TimeSpan?), "How long typing pauses before OnInput runs.", "a TimeSpan: 250.Milliseconds"),
        ]),
        new("Ui.SelectSearch", typeof(UiSelectSearch), "flux:select.search", "The listbox's search field, as a child.",
        [
            new("Placeholder", typeof(string), "Shown while it is empty. \"Search...\" when unset."),
            new("Icon", typeof(Ui.IconName?), "The icon at its start. A magnifying glass when unset."),
            new("Clearable", typeof(bool?), "The button that empties it. Shown unless false."),
            new("Value", typeof(string), "The text, for a page that holds the search itself."),
            new("OnInput", typeof(Callback<string>), "Runs with the text as typed, or once typing has paused for Debounce.", Picked),
            new("Debounce", typeof(TimeSpan?), "How long typing pauses before OnInput runs.", "a TimeSpan: 250.Milliseconds"),
        ]),
        new("Ui.SelectOptionCreate", typeof(UiSelectOptionCreate), "flux:select.option.create", "The row that offers to make an option of what was typed. Its children are its words.",
        [
            new("MinLength", typeof(int?), "How long the search must be before it is offered."),
            new("OnClick", typeof(Callback<string>), "Runs with the search as typed when it is picked.", "a handler given the text"),
        ]),
        new("Ui.SelectOptionEmpty", typeof(UiSelectOptionEmpty), "flux:select.option.empty", "What the list says when a search finds nothing. Its children are its words.",
        [
            new("WhenLoading", typeof(string), "What it says while results are on their way."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Checkbox { get; } =
    [
        new("Ui.Checkbox", typeof(UiCheckbox), "flux:checkbox", "A checkbox in a label: a bool on its own, a value in a group. Its children make a custom card.",
        [
            new("Bind", typeof(Expression<Func<bool?>>), "The model's flag it reads and writes: () => m.Agreed. Or Checked with OnChange.", "() => a bool or a bool?"),
            new("Checked", typeof(bool?), "Whether it is ticked, for a page that holds it itself."),
            new("OnChange", typeof(Callback<bool>), "Runs with the new state when it is pressed.", Picked),
            new("AfterBind", typeof(Callback<bool>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<bool>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as it is pressed, not with the next action."),
            new("Value", typeof(object), "What the group's collection holds while it is ticked.", "a value of the group's type"),
            new("Label", typeof(string), "Its words, beside it."),
            new("Description", typeof(string), "Help text under its label."),
            new("Icon", typeof(Ui.IconName?), "An icon on a card or a button."),
            new("Indeterminate", typeof(bool?), "Draws a dash: some of what it stands for, not all."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Name", typeof(string), "What it is posted under in a form."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
            new("Attributes", typeof(IReadOnlyDictionary<string, string?>), "Attributes for the <input> itself.", Attrs),
        ]),
        new("Ui.CheckboxGroup", typeof(UiCheckboxGroup<string>), "flux:checkbox.group", "Checkboxes over one collection of what is ticked.",
        [
            new("Bind", typeof(Expression<Func<ICollection<string>>>), "The model's collection it reads and writes: () => m.Topics. Or Values with OnChange.", "() => a collection of T"),
            new("Value", typeof(ICollection<string>), "What is ticked, for a page that holds it itself. The step is Values.", "a collection of T"),
            new("OnChange", typeof(Callback<ICollection<string>>), "Runs with what is ticked after a press.", Picked),
            new("AfterBind", typeof(Callback<ICollection<string>>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<ICollection<string>>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as a checkbox is pressed, not with the next action."),
            new("Variant", typeof(Ui.CheckboxGroupVariant?), "How the checkboxes are drawn. A column when unset."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the checkboxes."),
            new("Disabled", typeof(bool?), "Takes every checkbox in it out of reach."),
            new("Invalid", typeof(bool?), "Error styling on every checkbox in it."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.CheckboxAll", typeof(UiCheckboxAll), "flux:checkbox.all", "Ticks or clears its whole group, and shows a dash while some are ticked.",
        [
            new("Label", typeof(string), "Its words, beside it."),
            new("Description", typeof(string), "Help text under its label."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
        ]),
        new("Ui.CheckboxIndicator", typeof(UiCheckboxIndicator), "flux:checkbox.indicator", "The box, placed among a custom card's own content.", []),
    ];

    public static IReadOnlyList<UiApiPart> Radio { get; } =
    [
        new("Ui.RadioGroup", typeof(UiRadioGroup<string>), "flux:radio.group", "Radios over one value. Its type is the type of their values.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The model's member it reads and writes: () => m.Plan. Or Value with OnChange.", "() => a T"),
            new("Value", typeof(string), "The chosen value, for a page that holds it itself.", "T, the type of the radios' values"),
            new("OnChange", typeof(Callback<string>), "Runs with the new value when a radio is chosen.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as a radio is chosen, not with the next action."),
            new("Variant", typeof(Ui.RadioGroupVariant?), "How the radios are drawn. A column when unset."),
            new("Size", typeof(Ui.RadioGroupSize?), "Sm for a smaller segmented row."),
            new("Indicator", typeof(bool?), "False leaves the dot off the cards."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("Description", typeof(string), "Help text between the label and the radios."),
            new("Invalid", typeof(bool?), "Error styling the form did not ask for."),
            new("Name", typeof(string), "What the radios are posted under. The group's id when unset."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.Radio", typeof(UiRadio), "flux:radio", "One choice in a label. Its children are a button's words, or a custom card.",
        [
            new("Value", typeof(object), "What the group's member becomes when it is chosen.", "a value of the group's type"),
            new("Label", typeof(string), "Its words, beside it."),
            new("Description", typeof(string), "Help text under its label."),
            new("Icon", typeof(Ui.IconName?), "An icon on a segment, a card or a button."),
            new("Checked", typeof(bool?), "Chooses it while the group holds no value."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
            new("Name", typeof(string), "What it is posted under, in place of the group's."),
            new("Attributes", typeof(IReadOnlyDictionary<string, string?>), "Attributes for the <input> itself.", Attrs),
        ]),
        new("Ui.RadioIndicator", typeof(UiRadioIndicator), "flux:radio.indicator", "The dot, placed among a custom card's own content.", []),
    ];

    public static IReadOnlyList<UiApiPart> Switch { get; } =
    [
        new("Ui.Switch", typeof(UiSwitch), "flux:switch", "A setting turned on or off: a checkbox with role=\"switch\".",
        [
            new("Bind", typeof(Expression<Func<bool>>), "The model's flag it reads and writes: () => m.Alerts. Or Value with OnChange.", "() => a bool"),
            new("Value", typeof(bool), "Whether it is on, for a page that holds it itself."),
            new("OnChange", typeof(Callback<bool>), "Runs with the new state when it is flipped.", Picked),
            new("AfterBind", typeof(Callback<bool>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<bool>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as soon as it is flipped, not with the next action."),
            new("Label", typeof(string), "Its words, in a field around it."),
            new("Description", typeof(string), "Help text under its label."),
            new("Align", typeof(Ui.SwitchAlign?), "Left puts it before its label. After it when unset."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
            new("Name", typeof(string), "What it is posted under in a form."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
            new("Attributes", typeof(IReadOnlyDictionary<string, string?>), "Attributes for the <input> itself.", Attrs),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Slider { get; } =
    [
        new("Ui.Slider", typeof(UiSlider<int>), "flux:slider", "A value along a track, or a range between two thumbs. Its children are its ticks.",
        [
            new("Bind", typeof(Expression<Func<int>>), "The model's number it reads and writes: () => m.Amount, or an array of two for a range.", "() => a number, or an array of two"),
            new("Value", typeof(int), "The value, for a page that holds it itself.", "a number, or an array of two"),
            new("OnChange", typeof(Callback<int>), "Runs with every step the thumb moves.", Picked),
            new("AfterBind", typeof(Callback<int>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<int>?), "A rule the value is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as the thumb moves, not with the next action."),
            new("Range", typeof(bool?), "Two thumbs, over an array of two."),
            new("Min", typeof(double?), "The lowest value. 0 when unset."),
            new("Max", typeof(double?), "The highest value. 100 when unset."),
            new("Step", typeof(double?), "How far one step moves the thumb. 1 when unset."),
            new("BigStep", typeof(double?), "How far Shift with an arrow, and Page Up or Page Down, move it."),
            new("MinStepsBetween", typeof(int?), "How many steps a range's two thumbs keep between them."),
            new("TickPosition", typeof(Ui.SliderTickPosition?), "Where the ticks sit: under the track when unset, or Inside it."),
            new("TrackClass", typeof(string), "Classes for the track: h-5."),
            new("ThumbClass", typeof(string), "Classes for the thumbs: size-5."),
            new("Disabled", typeof(bool?), "Dims it and takes it out of reach."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.SliderTick", typeof(UiSliderTick), "flux:slider.tick", "A mark at a value. Its children are its label; a line when it has none.",
        [
            new("Value", typeof(double), "The value it marks. Required."),
            new("Variant", typeof(Ui.SliderTickVariant?), "A line when unset, or a Dot."),
        ]),
    ];

    public static IReadOnlyList<UiApiPart> Otp { get; } =
    [
        new("Ui.Otp", typeof(UiOtp), "flux:otp", "A cell per character, and the code they spell as one string. Its children place the cells by hand.",
        [
            new("Bind", typeof(Expression<Func<string>>), "The model's code it reads and writes: () => m.Code. Or Value with OnChange.", "() => a string"),
            new("Value", typeof(string), "The code, for a page that holds it itself."),
            new("OnChange", typeof(Callback<string>), "Runs with the code as it is typed.", Picked),
            new("AfterBind", typeof(Callback<string>), "Runs after Bind has written the model.", Picked),
            new("Validate", typeof(Validator<string>?), "A rule the code is checked by when it is written.", Validates),
            new("Live", typeof(bool?), "Writes the model as it is typed, not with the next action."),
            new("OnComplete", typeof(Callback<string>), "Runs with the code each time its last cell is filled: where Flux submits.", "a handler given the code"),
            new("Length", typeof(int?), "How many cells it draws, when they are not placed by hand."),
            new("Mode", typeof(Ui.OtpMode?), "What a cell takes. Digits when unset."),
            new("Private", typeof(bool?), "Masks each character, as a password does."),
            new("Autocomplete", typeof(string), "The first cell's autocomplete: \"off\" stops the browser offering a code. one-time-code when unset."),
            new("Label", typeof(string), "Draws a field around it, with this label and the error."),
            new("LabelSrOnly", typeof(bool?), "Keeps the label for screen readers only."),
            new("DescriptionTrailing", typeof(string), "Help text under the cells."),
            new("ErrorIcon", typeof(bool?), "False for the message under the cells without its icon."),
            new("ErrorClass", typeof(string), "Classes for the message under the cells: text-center."),
            new("Disabled", typeof(bool?), "Disables every cell."),
            new("Name", typeof(string), "What the code is posted under in a form."),
            new("ShowValidation", typeof(bool?), "False leaves the member's message to a Ui.Error placed elsewhere."),
        ]),
        new("Ui.OtpInput", typeof(UiOtpInput), "flux:otp.input", "One cell, placed by hand.", []),
        new("Ui.OtpSeparator", typeof(UiOtpSeparator), "flux:otp.separator", "A dash between cells or groups.", []),
        new("Ui.OtpGroup", typeof(UiOtpGroup), "flux:otp.group", "Cells joined into one box.", []),
    ];
}
