using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's input and its group as C# components: each example with its own code, and its reference.</summary>
[Route("input")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiInputPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Input.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Input)
            .Lead("A line of text, of any input type the browser has, bound to the model or held by the page. Everything "
                  + "inside the box (an icon, a shortcut, the clear, copy and reveal buttons) is a step, and a group joins "
                  + "a prefix, a suffix or a button to it.")
            .Api(UiApi.Input)[
            UiDocsExample.Key("ui-input").Heading("Input").File("InputExample.cs").Demo(InputExample).Testid("ui-input")
                .Blurb("In a field of its own making: a label, a description and the error, around the input."),
            UiDocsExample.Key("ui-input-shorthand").Heading("Shorthand").File("InputShorthandExample.cs").Demo(InputShorthandExample).Testid("ui-input-shorthand")
                .Blurb("Label and Description on the input draw the same field, with its error."),
            UiDocsExample.Key("ui-input-class").Heading("Class targeting").File("InputClassTargetingExample.cs").Demo(InputClassTargetingExample).Testid("ui-input-class")
                .Blurb("The input is a wrapper around an <input>: Class goes on the wrapper, InputClass on the <input> itself."),
            UiDocsExample.Key("ui-input-types").Heading("Types").File("InputTypesExample.cs").Demo(InputTypesExample).Testid("ui-input-types")
                .Blurb("The browser's input types, email, password, date and the rest, from Type."),
            UiDocsExample.Key("ui-input-file").Heading("File").File("InputFileExample.cs").Demo(InputFileExample).Testid("ui-input-file")
                .Blurb("A file input with a \"Choose files\" button and the chosen name; OnFiles hands the page what was chosen."),
            UiDocsExample.Key("ui-input-sm").Heading("Smaller").File("InputSmallerExample.cs").Demo(InputSmallerExample).Testid("ui-input-sm"),
            UiDocsExample.Key("ui-input-disabled").Heading("Disabled").File("InputDisabledExample.cs").Demo(InputDisabledExample).Testid("ui-input-disabled"),
            UiDocsExample.Key("ui-input-readonly").Heading("Readonly").File("InputReadonlyExample.cs").Demo(InputReadonlyExample).Testid("ui-input-readonly")
                .Blurb("For a value that may be read and copied but not changed, here drawn filled."),
            UiDocsExample.Key("ui-input-invalid").Heading("Invalid").File("InputInvalidExample.cs").Demo(InputInvalidExample).Testid("ui-input-invalid")
                .Blurb("Error styling the form did not ask for. A bound input is marked invalid on its own."),
            UiDocsExample.Key("ui-input-mask").Heading("Input masking").File("InputMaskExample.cs").Demo(InputMaskExample).Testid("ui-input-mask")
                .Blurb("Each keystroke is held to the pattern: 9 is a digit, a a letter, * either. Money() groups an amount "
                       + "as it is typed, and a bound number receives the amount it spells."),
            UiDocsExample.Key("ui-input-icons").Heading("Icons").File("InputIconsExample.cs").Demo(InputIconsExample).Testid("ui-input-icons"),
            UiDocsExample.Key("ui-input-icon-buttons").Heading("Icon buttons").File("InputIconButtonsExample.cs").Demo(InputIconButtonsExample).Testid("ui-input-icon-buttons")
                .Blurb("IconTrailing also takes content of your own, such as a button."),
            UiDocsExample.Key("ui-input-clearable").Heading("Clearable, copyable, and viewable inputs").File("InputClearableExample.cs").Demo(InputClearableExample).Testid("ui-input-clearable")
                .Blurb("Three buttons the input draws itself: one that empties it, one that reveals a password, one that copies the value."),
            UiDocsExample.Key("ui-input-kbd").Heading("Keyboard hint").File("InputKbdExample.cs").Demo(InputKbdExample).Testid("ui-input-kbd"),
            UiDocsExample.Key("ui-input-button").Heading("As a button").File("InputAsButtonExample.cs").Demo(InputAsButtonExample).Testid("ui-input-button")
                .Blurb("A <button> drawn as the input, for a search that opens a palette."),
            UiDocsExample.Key("ui-input-group").Heading("With buttons").File("InputWithButtonsExample.cs").Demo(InputWithButtonsExample).Testid("ui-input-group")
                .Blurb("An input group joins its neighbours into one outline: the two ends keep their corners and a border "
                       + "is drawn once between them."),
            UiDocsExample.Key("ui-input-affixes").Heading("Text prefixes and suffixes").File("InputAffixesExample.cs").Demo(InputAffixesExample).Testid("ui-input-affixes"),
            UiDocsExample.Key("ui-input-group-label").Heading("Input group labels").File("InputGroupLabelExample.cs").Demo(InputGroupLabelExample).Testid("ui-input-group-label")
                .Blurb("For a label, the group goes in a field, which reaches the input inside it."),
            UiInputBehaviour
        ];
}
