using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's textarea as a C# component: each example with its own code, and its reference.</summary>
[Route("textarea")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTextareaPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Textarea.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Textarea)
            .Lead("Several lines of text, for comments, descriptions and feedback: four rows unless you say otherwise, or "
                  + "as tall as what is typed in it.")
            .Api(UiApi.Textarea)[
            UiDocsExample.Key("ui-textarea").Heading("Textarea").File("TextareaExample.cs").Demo(TextareaExample).Testid("ui-textarea-bare"),
            UiDocsExample.Key("ui-textarea-placeholder").Heading("With placeholder").File("TextareaPlaceholderExample.cs").Demo(TextareaPlaceholderExample).Testid("ui-textarea")
                .Blurb("A hint inside the box on what to write, and a label over it."),
            UiDocsExample.Key("ui-textarea-rows").Heading("Fixed row height").File("TextareaRowsExample.cs").Demo(TextareaRowsExample).Testid("ui-textarea-rows"),
            UiDocsExample.Key("ui-textarea-auto").Heading("Auto-sizing textarea").File("TextareaAutoExample.cs").Demo(TextareaAutoExample).Testid("ui-textarea-auto")
                .Blurb("Rows(UiTextareaRows.Auto) grows with what is typed. It is CSS's field-sizing, which not every "
                       + "browser has yet: elsewhere it keeps its height."),
            UiDocsExample.Key("ui-textarea-resize").Heading("Configure resize").File("TextareaResizeExample.cs").Demo(TextareaResizeExample).Testid("ui-textarea-resize")
                .Blurb("Which way the reader may drag it: down, not at all, across, or both."),
            UiTextareaBehaviour
        ];
}
