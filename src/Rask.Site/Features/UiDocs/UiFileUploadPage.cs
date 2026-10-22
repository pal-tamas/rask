using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's file upload and file items as C# components: each example with its own code, and its reference.</summary>
[Route("file-upload")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiFileUploadPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.FileUpload.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.FileUpload)
            .Lead("An area that takes files, clicked, reached by keyboard or dropped on, and a row for each file. Where "
                  + "Flux binds a Livewire property, the files come to the page through OnFiles, and the page draws the "
                  + "list from its own state.")
            .Api(UiApi.FileUpload)[
            UiDocsExample.Key("ui-upload").Heading("File upload").File("FileUploadExample.cs").Demo(FileUploadExample).Testid("ui-upload")
                .Blurb("A picture is read in OnFiles and shown as its own preview; the cross takes a file off the page's list."),
            UiDocsExample.Key("ui-upload-inline").Heading("Inline layout").File("FileUploadInlineExample.cs").Demo(FileUploadInlineExample).Testid("ui-upload-inline")
                .Blurb("The icon beside the words, for a more compact form."),
            UiDocsExample.Key("ui-upload-progress").Heading("Progress indicator").File("FileUploadProgressExample.cs").Demo(FileUploadProgressExample).Testid("ui-upload-progress")
                .Blurb("A bar in place of the text while files upload. In a WebAssembly app nothing is sent, so the bar is "
                       + "how much of the files the handler has read."),
            UiDocsExample.Key("ui-upload-disabled").Heading("Disabled state").File("FileUploadDisabledExample.cs").Demo(FileUploadDisabledExample).Testid("ui-upload-disabled")
                .Blurb("No click, no key and no drop, and the dropzone drawn muted."),
            UiDocsExample.Key("ui-upload-custom").Heading("Custom uploader").File("FileUploadCustomExample.cs").Demo(FileUploadCustomExample).Testid("ui-upload-custom")
                .Blurb("Markup of your own in place of the dropzone, with the same input behind it. in-data-dragging: and "
                       + "in-data-loading: style the two states."),
            UiDocsExample.Key("ui-upload-single").Heading("Single file upload").File("FileUploadSingleExample.cs").Demo(FileUploadSingleExample).Testid("ui-upload-single")
                .Blurb("One file, kept by the page until it is saved or removed."),
            UiDocsExample.Key("ui-upload-multiple").Heading("Multiple files upload").File("FileUploadMultipleExample.cs").Demo(FileUploadMultipleExample).Testid("ui-upload-multiple")
                .Blurb("Several files, each with its own remove button named after it."),
            UiFileUploadBehaviour
        ];
}
