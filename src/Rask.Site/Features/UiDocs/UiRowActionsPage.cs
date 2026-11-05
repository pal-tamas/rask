using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's row actions — not a Flux component, an addition the owner approved — its example with its code, and its
///     reference.
/// </summary>
[Route("row-actions")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiRowActionsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.RowActions.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.RowActions)
            .Lead("What a list row can do, at its end: a pencil that opens it and a bin that deletes it, drawn as Flux "
                  + "draws a row's action — a ghost, small, square button — with the outline drawing in the muted ink. "
                  + "Not one of Flux UI's components: the kit's own, worded from the RaskStrings catalog.")
            .Api(UiApi.RowActions)[
            UiDocsExample.Key("ui-row-actions").Heading("Edit and delete").File("RowActionsExample.cs").Demo(RowActionsExample).Testid("ui-row-actions")
                .Blurb("Edit(url) is where the pencil goes. Remove(delete, heading, question) adds the bin, which asks in "
                       + "the layout's Ui.Confirm and deletes only on yes; a row that cannot be deleted passes null and has "
                       + "none. What the row puts inside stands before the pencil. It is the row's last cell itself; its column is Width(Ui.ColumnWidth.Actions).")
        ];
}
