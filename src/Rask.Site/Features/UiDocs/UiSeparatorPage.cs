using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's separator as a C# component: each example with its own code, and its reference.</summary>
[Route("separator")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSeparatorPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Separator.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Separator)
            .Lead("A line between sections of content or groups of items. It carries no margin — the page "
                  + "spaces it — and a vertical one is as tall as its row until a vertical margin shortens it.")
            .Api(UiApi.Separator)[
            UiDocsExample.Key("ui-separator-intro").Heading("Separator").File("SeparatorExample.cs").Demo(SeparatorExample).Testid("ui-separator-intro"),
            UiDocsExample.Key("ui-separator-text").Heading("With text").File("SeparatorTextExample.cs").Demo(SeparatorTextExample).Testid("ui-separator-text"),
            UiDocsExample.Key("ui-separator-vertical").Heading("Vertical").File("SeparatorVerticalExample.cs").Demo(SeparatorVerticalExample).Testid("ui-separator-vertical")
                .Blurb("As tall as its row."),
            UiDocsExample.Key("ui-separator-limited").Heading("Limited height").File("SeparatorLimitedExample.cs").Demo(SeparatorLimitedExample).Testid("ui-separator-limited")
                .Blurb("A vertical margin shortens it."),
            UiDocsExample.Key("ui-separator-subtle").Heading("Subtle").File("SeparatorSubtleExample.cs").Demo(SeparatorSubtleExample).Testid("ui-separator-subtle")
                .Blurb("A fainter line.")
        ];
}
