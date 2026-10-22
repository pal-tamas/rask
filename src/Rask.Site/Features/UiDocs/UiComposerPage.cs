using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's composer as a C# component: each example with its own code, and its reference.</summary>
[Route("composer")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiComposerPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Composer.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Composer)
            .Lead("A message box with buttons around it, for a chat or an AI prompt. It sends the form it is in from the "
                  + "keyboard, and what was typed travels with that submit.")
            .Api(UiApi.Composer)[
            UiDocsExample.Key("ui-composer").Heading("Composer").File("ComposerExample.cs").Demo(ComposerExample).Testid("ui-composer")
                .Blurb("Type a few lines and press Ctrl+Enter or Cmd+Enter: the form is sent, and the box grows a line at a time until then."),
            UiDocsExample.Key("ui-composer-header").Heading("With header").File("ComposerHeaderExample.cs").Demo(ComposerHeaderExample).Testid("ui-composer-header")
                .Blurb("A file about to be sent, above the box."),
            UiDocsExample.Key("ui-composer-inline").Heading("Inline").File("ComposerInlineExample.cs").Demo(ComposerInlineExample).Testid("ui-composer-inline")
                .Blurb("The buttons in one row with the box, the leading ones before it."),
            UiDocsExample.Key("ui-composer-input").Heading("Input variant").File("ComposerInputVariantExample.cs").Demo(ComposerInputVariantExample).Testid("ui-composer-input")
                .Blurb("The corners of the other inputs."),
            UiDocsExample.Key("ui-composer-height").Heading("Height").File("ComposerHeightExample.cs").Demo(ComposerHeightExample).Testid("ui-composer-height")
                .Blurb("Four lines to start with, eight before it scrolls."),
            UiDocsExample.Key("ui-composer-enter").Heading("Submit behavior").File("ComposerSubmitExample.cs").Demo(ComposerSubmitExample).Testid("ui-composer-enter")
                .Blurb("Enter alone sends the form, and Shift+Enter is a new line."),
            UiDocsExample.Key("ui-composer-rich").Heading("Rich text").File("ComposerRichTextExample.cs").Demo(ComposerRichTextExample).Testid("ui-composer-rich")
                .Blurb("An editor in the Input slot stands for the box, drawn without a box of its own and bound to the same value."),
            UiDocsExample.Key("ui-composer-disabled").Heading("Disabled").File("ComposerDisabledExample.cs").Demo(ComposerDisabledExample).Testid("ui-composer-disabled")
                .Blurb("The box and every button in it are locked."),
            UiDocsExample.Key("ui-composer-invalid").Heading("Invalid").File("ComposerInvalidExample.cs").Demo(ComposerInvalidExample).Testid("ui-composer-invalid")
                .Blurb("Send it empty: the form holds a message for the prompt, and the composer is drawn invalid."),
            UiComposerBehaviour
        ];
}
