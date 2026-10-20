using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's toast as a C# component: each example with its own code, and its reference.</summary>
[Route("toast")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiToastPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Toast.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Toast)
            .Lead("A toast is raised, not placed: Toast.Success(\"Saved\") from any handler, with nothing injected. "
                  + "Ui.Toast in the layout is where toasts appear, and the last example below places the one every "
                  + "toast on this page shows in.")
            .Api(UiApi.Toast)[
            UiDocsExample.Key("ui-toast").Heading("Toast").File("ToastExample.cs").Demo(ToastExample).Testid("ui-toast")
                .Blurb("Toast.Info, Toast.Success, Toast.Warning and Toast.Error say it; Flux's Flux::toast()."),
            UiDocsExample.Key("ui-toast-heading").Heading("With heading").File("ToastHeadingExample.cs").Demo(ToastHeadingExample).Testid("ui-toast-heading")
                .Blurb("A heading gives the toast more context; the message becomes the line under it."),
            UiDocsExample.Key("ui-toast-variants").Heading("Variants").File("ToastVariantsExample.cs").Demo(ToastVariantsExample).Testid("ui-toast-variants")
                .Blurb("Success, warning and danger, each said by the shape of its icon as well as its colour."),
            UiDocsExample.Key("ui-toast-actions").Heading("Actions").File("ToastActionsExample.cs").Demo(ToastActionsExample).Testid("ui-toast-actions")
                .Blurb("One action: its handler runs, with a spinner while it does, and then the toast goes."),
            UiDocsExample.Key("ui-toast-links").Heading("Links").File("ToastLinksExample.cs").Demo(ToastLinksExample).Testid("ui-toast-links")
                .Blurb("A link that gives a clear next step, and stays a real link."),
            UiDocsExample.Key("ui-toast-duration").Heading("Duration").File("ToastDurationExample.cs").Demo(ToastDurationExample).Testid("ui-toast-duration")
                .Blurb("How long it stays before it goes by itself. Five seconds when it says nothing."),
            UiDocsExample.Key("ui-toast-permanent").Heading("Permanent").File("ToastPermanentExample.cs").Demo(ToastPermanentExample).Testid("ui-toast-permanent")
                .Blurb("UntilDismissed() keeps it until its close button, or Escape, takes it down."),
            UiDocsExample.Key("ui-toast-placement").Heading("Placement").File("ToastPlacementExample.cs").Demo(ToastPlacementExample).Testid("ui-toast-placement")
                .Blurb("Flux's inverted look, another corner, and a stack: what the layout places decides. Pick one, "
                       + "then raise toasts from the examples above."),
            UiToastBehaviour
        ];
}
