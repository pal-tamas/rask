namespace Rask.Site;

/// <summary>
///     The moon in the top bar: one click flips the page between light and dark, as on fluxui.dev.
/// </summary>
/// <remarks>
///     Flux's own header: a subtle <c>Ui.Button</c> holding the moon, its resting ink set to zinc-300 in
///     both schemes as Flux's docs set theirs, and the button's own tooltip below it, which names it and
///     the <kbd>D</kbd> shortcut — <c>tooltip</c>, <c>tooltip:kbd</c> and <c>tooltip:position</c> there. It calls
///     <c>Rask.dark</c>, which <c>Ui.AppearanceScript</c> defines — no C# handler, so it works before
///     the app has booted and costs no handler id.
/// </remarks>
internal sealed partial class AppearanceToggle : Component
{
    private const string Label = "Toggle dark mode";

    // Only at rest: under the pointer the button's own subtle hover takes over, in either scheme.
    private const string RestingInk = "not-hover:text-zinc-300";

    // The shortcut the tooltip advertises. Not while typing, and not with a modifier held.
    private const string Shortcut =
        "(function(){if(window.raskAppearanceKey)return;window.raskAppearanceKey=true;"
        + "document.addEventListener('keydown',function(e){var t=e.target;"
        + "if(e.key!=='d'||e.metaKey||e.ctrlKey||e.altKey||e.repeat||!window.Rask)return;"
        + "if(t&&(t.isContentEditable||/^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)))return;"
        + "window.Rask.dark=!window.Rask.dark;});})();";

    /// <inheritdoc />
    protected override Component? HeadAssets => Script[Raw.Value(Shortcut)];

    /// <inheritdoc />
    protected override Component? Render() =>
        Ui.Button.Subtle.Icon(Ui.IconName.Moon)
            .Tooltip(Label).TooltipKbd("D").TooltipPosition(Ui.TooltipPosition.Bottom)
            .Class(RestingInk)
            .AriaKeyShortcuts("D")
            .Attributes(("onclick", "Rask.dark=!Rask.dark"))
            .Data(new AttrBag("appearance-toggle", ""));
}
