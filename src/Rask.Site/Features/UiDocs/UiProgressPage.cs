using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's progress bar as a C# component: each example with its own code, and its reference.</summary>
[Route("progress")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiProgressPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Progress.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Progress)
            .Lead("Work in progress with a known amount left: a track and the bar filling it, announced as a "
                  + "progressbar with its value and maximum, and moving to a new value rather than jumping.")
            .Api(UiApi.Progress)[
            UiDocsExample.Key("ui-progress").Heading("Progress").File("ProgressExample.cs").Demo(ProgressExample).Testid("ui-progress")
                .Blurb("Name it for a screen reader with Aria(\"label\", …), or Aria(\"labelledby\", id)."),
            UiDocsExample.Key("ui-progress-max").Heading("Max").File("ProgressMaxExample.cs").Demo(ProgressMaxExample).Testid("ui-progress-max")
                .Blurb("A scale other than a hundred: three steps of seven."),
            UiDocsExample.Key("ui-progress-color").Heading("Color").File("ProgressColorExample.cs").Demo(ProgressColorExample).Testid("ui-progress-color")
                .Blurb("Any Tailwind hue in place of the accent."),
            UiDocsExample.Key("ui-progress-height").Heading("Height").File("ProgressHeightExample.cs").Demo(ProgressHeightExample).Testid("ui-progress-height")
                .Blurb("A class makes it taller."),
            UiDocsExample.Key("ui-progress-label").Heading("With label").File("ProgressLabelExample.cs").Demo(ProgressLabelExample).Testid("ui-progress-label")
                .Blurb("A label above it names it, and a line below says what is happening."),
            UiDocsExample.Key("ui-progress-controlled").Heading("Controlled").File("ProgressControlledExample.cs").Demo(ProgressControlledExample).Testid("ui-progress-controlled")
                .Blurb("The value is the page's field, shown beside the label; the bar moves to each new one."),
            UiProgressBehaviour
        ];
}
