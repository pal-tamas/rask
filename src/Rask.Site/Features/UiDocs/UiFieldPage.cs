using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's field and its parts as C# components: each example with its own code, and its reference.</summary>
[Route("field")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiFieldPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Field.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Field)
            .Lead("A label, a control, help text and the message the form holds for it, stacked and wired together: the "
                  + "label is the control's, and the help text and the message are what a screen reader reads with it.")
            .Api(UiApi.Field)[
            UiDocsExample.Key("ui-field").Heading("Field").File("FieldExample.cs").Demo(FieldExample).Testid("ui-field")
                .Blurb("The page decides the address is wrong and says why: the message shows under the input, and the "
                       + "input is marked invalid."),
            UiDocsExample.Key("ui-field-shorthand").Heading("Shorthand props").File("FieldShorthandExample.cs").Demo(FieldShorthandExample).Testid("ui-field-shorthand")
                .Blurb("Every form control takes Label and Description itself, and draws the field and its error around it."),
            UiDocsExample.Key("ui-field-trailing").Heading("With trailing description").File("FieldTrailingDescriptionExample.cs").Demo(FieldTrailingDescriptionExample).Testid("ui-field-trailing")
                .Blurb("A description after the control sits under it. DescriptionTrailing says the same on the control."),
            UiDocsExample.Key("ui-field-badge").Heading("With badge").File("FieldBadgeExample.cs").Demo(FieldBadgeExample).Testid("ui-field-badge")
                .Blurb("A badge beside the label says what a reader might not expect: \"Required\", \"Optional\"."),
            UiDocsExample.Key("ui-field-split").Heading("Split layout").File("FieldSplitExample.cs").Demo(FieldSplitExample).Testid("ui-field-split")
                .Blurb("Two fields in one row are a grid of yours."),
            UiDocsExample.Key("ui-field-fieldset").Heading("Fieldset").File("FieldFieldsetExample.cs").Demo(FieldFieldsetExample).Testid("ui-field-fieldset")
                .Blurb("Related fields under one legend, in a real fieldset."),
            UiDocsExample.Key("ui-bound").Heading("Bound to a model").File("FieldBoundExample.cs").Demo(FieldBoundExample).Testid("ui-bound")
                .Blurb("Rask's own: the same controls with no handler between them and the model. Bind writes back, "
                       + "and a bound int is a number field with nothing said at the call site."),
            UiFieldBehaviour
        ];
}
