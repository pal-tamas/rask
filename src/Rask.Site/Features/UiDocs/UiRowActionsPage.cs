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
            .Lead("What a list row can do, at its end, behind one ⋯: the menu Flux's table demo opens from each row — "
                  + "look at it, edit it, what the page adds, and delete it, asking first. "
                  + "Not one of Flux UI's components: the kit's own, worded from the RaskStrings catalog.")
            .Api(UiApi.RowActions)[
            UiDocsExample.Key("ui-row-actions").Heading("Edit and delete").File("RowActionsExample.cs").Demo(RowActionsExample).Testid("ui-row-actions")
                .Blurb("Edit(url) is where the menu's edit item goes. Remove(delete, heading, question) adds the delete, "
                       + "last and in red, which asks in the layout's Ui.Confirm and deletes only on yes; a row that cannot "
                       + "be deleted passes null and has none. It is the row's last cell itself; its column is Ui.TableColumn.RowMenu."),
            UiDocsExample.Key("ui-row-actions-menu").Heading("View, and what the row adds").File("RowActionsMenuExample.cs").Demo(RowActionsMenuExample).Testid("ui-row-actions-menu")
                .Blurb("A ghost ⋯ opening at the bottom end, as Flux's table demo does, holds View(url), "
                       + "Edit(url) — or OnEdit to edit the row where it stands — what the row puts inside, then the delete, "
                       + "apart and in red, asking first. None is required; a row nothing can be done with has no ⋯. The cell "
                       + "keeps to the table's right edge while the other columns scroll under it; its column is "
                       + "Ui.TableColumn.RowMenu. Inside a keyed row it needs no key of its own: a press that arrives "
                       + "after a row above it went still reaches its own row.")
        ];
}
