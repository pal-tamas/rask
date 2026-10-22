namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorCustomizationExample : Component
{
    private string _last = "nothing yet";

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Editor[
                Ui.EditorToolbar[
                    Ui.EditorHeading, Ui.EditorSeparator,
                    Ui.EditorBold, Ui.EditorItalic, Ui.EditorStrike, Ui.EditorSeparator,
                    Ui.EditorBullet, Ui.EditorOrdered, Ui.EditorBlockquote, Ui.EditorSeparator,
                    Ui.EditorLink, Ui.EditorSeparator,
                    Ui.EditorAlign,
                    Ui.EditorSpacer,
                    Ui.Dropdown.Position(Ui.DropdownPosition.Bottom).Align(Ui.DropdownAlign.End).Offset(-15)[
                        Ui.EditorButton.Icon(Ui.IconName.EllipsisHorizontal).Tooltip("More"),
                        Ui.Menu[
                            Ui.MenuItem.Icon(Ui.IconName.ArrowTopRightOnSquare).OnClick(() => { _last = "preview"; })["Preview"],
                            Ui.MenuItem.Icon(Ui.IconName.ArrowDownTray).OnClick(() => { _last = "export"; })["Export"],
                            Ui.MenuItem.Icon(Ui.IconName.Share).OnClick(() => { _last = "share"; })["Share"]
                        ]
                    ]
                ],
                Ui.EditorContent
            ],
            Ui.Text.Data("testid", "ui-editor-menu")[$"Last action: {_last}."]
        ];
}
