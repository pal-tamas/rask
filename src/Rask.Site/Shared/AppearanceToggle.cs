namespace Rask.Site;

/// <summary>
///     The moon in the top bar: one click flips the page between light and dark, as on fluxui.dev.
/// </summary>
/// <remarks>
///     Measured off Flux's own header: a 40px square subtle button, an 8px radius, the 20px <c>mini</c>
///     moon in zinc-300 in both schemes, a tooltip below it naming the <kbd>D</kbd> shortcut. It calls
///     <c>Rask.dark</c>, which <c>Ui.AppearanceScript</c> defines — no C# handler, so it works before
///     the app has booted and costs no handler id.
/// </remarks>
internal sealed partial class AppearanceToggle : Component
{
    private const string Label = "Toggle dark mode";

    // SEAM: swap this <button> + tooltip for
    //   Ui.Button.Subtle.Square.Icon(Ui.IconName.Moon).Tooltip(Label).TooltipKbd("D")
    // (keeping the onclick and the zinc-300 resting colour) when Flux's Ui.Button and Ui.Tooltip land.
    private const string ButtonClass =
        "inline-flex size-10 items-center justify-center rounded-lg bg-transparent text-zinc-300 "
        + "hover:bg-zinc-800/5 hover:text-zinc-800 dark:hover:bg-white/15 dark:hover:text-white";

    // Anchored to the button's end rather than centred under it: the moon is the last thing in the bar,
    // and a centred tooltip would hang off a phone's edge.
    private const string TooltipClass =
        "pointer-events-none absolute end-0 top-full z-10 mt-[5px] hidden whitespace-nowrap rounded-md "
        + "bg-zinc-800 px-2.5 py-2 text-xs font-medium text-white group-hover:block "
        + "group-has-focus-visible:block dark:border dark:border-white/10 dark:bg-zinc-700";

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
        Span.Class("group relative inline-flex")[
            Button
                .Type(ButtonType.Button)
                .Class(ButtonClass)
                .AriaLabel(Label)
                .AriaKeyShortcuts("D")
                .Attributes(("onclick", "Rask.dark=!Rask.dark"))
                .Data(new AttrBag("appearance-toggle", ""))[
                Ui.Icon.Name(Ui.IconName.Moon).Mini
            ],
            Span.Class(TooltipClass).Aria("hidden", "true")[
                Label,
                Span.Class("ps-1 text-zinc-300")["D"]
            ]
        ];
}
