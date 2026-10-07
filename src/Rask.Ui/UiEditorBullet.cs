namespace Rask;

/// <summary>
///     A bulleted list: the <c>bullet</c> item of an editor's toolbar, Flux's <c>flux:editor.bullet</c>.
/// </summary>
public sealed partial class UiEditorBullet : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiEditorMarkup.Toggle("bullet", "Bullet list", null, UiEditorIcons.Hero(Ui.IconName.ListBullet));
}
