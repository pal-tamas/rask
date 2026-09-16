using System.Globalization;
using Rask.Core.Live;

namespace Rask.Ui;

/// <summary>
/// A control that opens a menu beside it: the popover, the placement, and the keyboard cursor.
/// </summary>
/// <remarks>
/// <para>
/// What <see cref="UiDropdown" /> and <see cref="UiProfile" /> share, which is everything except what the
/// button LOOKS like. The panel is a <c>[popover]</c>, so the browser owns opening and closing it: the top
/// layer, so no <c>overflow: hidden</c> ancestor clips it; Escape and a click outside to dismiss it; and focus
/// handed back to the trigger when it closes. C# owns only the keyboard cursor, the way Flux UI's menus
/// behave — the arrow keys move it, Home and End jump, typing a letter jumps to the next item starting with
/// it, ArrowRight opens a submenu and ArrowLeft closes it, Enter or Space picks, Tab leaves — and the cursor
/// is <c>aria-activedescendant</c> on the menu, so focus never leaves the list while it moves.
/// </para>
/// <para>
/// It is a base class rather than a shared helper because the keyboard contract is the part nobody should be
/// able to get half of. A second menu control that reimplemented the arrows would be a menu whose arrows
/// disagreed with every other menu in the kit — exactly the inconsistency the cursor exists to prevent.
/// </para>
/// </remarks>
public abstract partial class UiMenuButton : Component
{
    // Per instance, so two menus on one page cannot collide on item ids — aria-activedescendant names them.
    // Shared across every subclass, so a dropdown and a profile on one page cannot collide either.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);
    private readonly HashSet<int> _openSubs = [];

    private bool _open;
    private int _cursor = -1;
    private UiMenuScope? _scope;
    private UiTypeAhead _typeAhead;

    /// <summary>Which side of the trigger the menu opens on. Below, unless this says otherwise.</summary>
    public UiPosition? Position { get; set; }

    /// <summary>Where along that side the menu sits — flush with the trigger's start, centred, or its end.</summary>
    public UiAlign? Align { get; set; }

    /// <summary>The distance between the trigger and the menu, in pixels. Defaults to 4.</summary>
    public int? Gap { get; set; }

    /// <summary>Shifts the menu along its alignment, in pixels.</summary>
    public int? Offset { get; set; }

    /// <summary>
    ///     Whether the menu is open. Leave it unset to let the reader open and close it; set it to take
    ///     ownership, and pair it with <see cref="OnToggle" />.
    /// </summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the reader opens or closes it, with the state it is now in.</summary>
    public Callback<bool>? OnToggle { get; set; }

    /// <summary>
    ///     Keeps the menu open after an item is picked — for a menu of checkboxes, where choosing three should not
    ///     mean opening it three times. An item can ask for the same on its own.
    /// </summary>
    public bool? KeepOpen { get; set; }

    public string? Class { get; set; }

    // The clock the type-ahead's prefix expires by. Internal, so it is not a chain step; a test hands it its own.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    // The cursor and the open submenus are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <summary>What this control's generated ids start with, so each kind is recognisable in the markup.</summary>
    private protected abstract string PrefixTag { get; }

    /// <summary>The classes on the button itself.</summary>
    private protected abstract string TriggerClass { get; }

    /// <summary>The classes on the wrapper the popover is anchored inside.</summary>
    private protected virtual string RootClass => "relative inline-block";

    /// <summary>Which way the menu opens when the call site did not say.</summary>
    private protected virtual UiPosition DefaultPosition => UiPosition.Bottom;

    /// <summary>The width of the menu panel.</summary>
    private protected virtual string MenuClass => "menu w-56 p-0";

    private protected string Prefix => PrefixTag + "-" + _instance.ToString(CultureInfo.InvariantCulture);

    private protected string TriggerId => Prefix + "-trigger";

    private protected string PanelId => Prefix + "-panel";

    private protected string MenuId => Prefix + "-menu";

    private protected bool IsOpen => Open ?? _open;

    /// <summary>What goes inside the button — the label and its icons, or a whole profile row.</summary>
    private protected abstract Component TriggerContent();

    /// <summary>The trigger, the popover and the menu: the shape every menu control in the kit renders.</summary>
    private protected Component MenuButton()
    {
        var open = IsOpen;
        _scope = new UiMenuScope(Prefix, open ? _cursor : -1, _openSubs, KeepOpen == true, ToggleSubAsync);

        var trigger = Button
            .Id(TriggerId)
            .Type("button")
            .Class(TriggerClass)
            .Aria(new Dictionary<string, string?>
            {
                ["haspopup"] = "menu",
                ["expanded"] = open ? "true" : "false",
                ["controls"] = MenuId,
            })
            .Attributes(("popovertarget", PanelId), ("style", "anchor-name:--" + Prefix));

        var panel = Div
            .Id(PanelId)
            .Popover("auto")
            .Class("z-1 rounded-box border border-base-300 bg-base-100 p-2 shadow-sm")
            .Attributes(("style", PanelStyle()))
            // The SOLE writer of the open state, as on UiSelect: the browser opens the popover from
            // `popovertarget` and closes it on Escape, a click outside, a pick and Tab, and this is where C# hears
            // which.
            .OnToggle(OnPanelToggleAsync);

        if (Open is { } controlled)
        {
            // The runtime shows or hides the popover to match whenever this changes (rask-dom.ts).
            panel = panel.Data("rask-popover-open", controlled ? "true" : "false");
        }

        var menu = Ul
            .Id(MenuId)
            .Role("menu")
            .TabIndex(-1)
            .Class(MenuClass)
            .Aria(open && _cursor >= 0
                ? new Dictionary<string, string?>
                {
                    ["labelledby"] = TriggerId,
                    ["activedescendant"] = _scope.ItemId(_cursor),
                }
                : new Dictionary<string, string?> { ["labelledby"] = TriggerId })
            // Popover focusing steps move focus to the autofocus descendant when it opens, so the menu is focused
            // and the arrow keys reach it straight from the click that opened it.
            .Attributes(KeepOpen == true
                ? [("autofocus", null), ("data-rask-keep-open", null)]
                : [("autofocus", null)])
            .OnKeyDown(OnKeyAsync);

        var root = Div.Class(UiClass.Compose(RootClass, Class));
        if (open)
        {
            // A styling hook, Flux's `data-open`: a trigger that looks pressed while its menu is up.
            root = root.Data("open", "");
        }

        return root[
            trigger[TriggerContent()],
            panel[menu[Context.Provide(new UiMenuLevel(_scope, -1))[Children ?? []]]]
        ];
    }

    // Placement by CSS anchor positioning, in a style attribute: nothing here is scanned by Tailwind, so the
    // values are built freely. An engine without anchor positioning keeps the popover's own default, centred —
    // still open and usable, the trade UiSelect and UiMegamenu already make.
    private string PanelStyle()
    {
        var side = (Position ?? DefaultPosition) switch
        {
            UiPosition.Top => "block-start",
            UiPosition.Left => "inline-start",
            UiPosition.Right => "inline-end",
            _ => "block-end",
        };
        var vertical = (Position ?? DefaultPosition) is UiPosition.Left or UiPosition.Right;
        var along = Align switch
        {
            UiAlign.Center => "center",
            UiAlign.End => vertical ? "span-block-start" : "span-inline-start",
            _ => vertical ? "span-block-end" : "span-inline-end",
        };
        var gap = (Gap ?? 4).ToString(CultureInfo.InvariantCulture);
        var offset = (Offset ?? 0).ToString(CultureInfo.InvariantCulture);

        return "position-anchor:--" + Prefix
               + ";position-area:" + side + " " + along
               + ";position-try-fallbacks:flip-block,flip-inline"
               + ";margin:" + gap + "px"
               + ";translate:" + (vertical ? "0 " + offset + "px" : offset + "px 0")
               // Submenus fly out beside their row, so the panel must not clip what overflows it.
               + ";overflow:visible";
    }

    private async Task OnPanelToggleAsync(ToggleEventArgs e)
    {
        _open = e.IsOpen;
        _openSubs.Clear();
        _cursor = e.IsOpen && _scope is { } scope ? FirstEnabled(scope.Entries, -1) : -1;

        // Controlled: tell the page only about a change it did not make itself — the runtime showing the popover
        // because Open became true fires this same event.
        if (OnToggle is { } onToggle && e.IsOpen != Open)
        {
            await (onToggle.Invoke(e.IsOpen) ?? Task.CompletedTask).ConfigureAwait(false);
        }
    }

    private Task ToggleSubAsync(int ordinal)
    {
        if (_scope is not { } scope || ordinal < 0 || ordinal >= scope.Entries.Count)
        {
            return Task.CompletedTask;
        }

        if (!_openSubs.Remove(ordinal))
        {
            _openSubs.Add(ordinal);
            _cursor = FirstEnabled(scope.Entries, ordinal) is var first and >= 0 ? first : ordinal;
        }
        else
        {
            CloseSubsFrom(scope.Entries, ordinal);
            _cursor = ordinal;
        }

        return Task.CompletedTask;
    }

    // ---- the keyboard ---------------------------------------------------------------------------

    private Task OnKeyAsync(KeyboardEventArgs e)
    {
        // A modified key belongs to the browser or the app, not to the menu.
        if (_scope is not { } scope || scope.Entries.Count == 0 || e.Ctrl || e.Alt || e.Meta)
        {
            return Task.CompletedTask;
        }

        var entries = scope.Entries;
        var at = _cursor >= 0 && _cursor < entries.Count ? _cursor : -1;
        var level = at >= 0 ? entries[at].Parent : -1;

        switch (e.Key)
        {
            case "ArrowDown":
                _cursor = Step(entries, level, at, +1);
                break;
            case "ArrowUp":
                _cursor = Step(entries, level, at, -1);
                break;
            case "Home" or "PageUp":
                _cursor = FirstEnabled(entries, level);
                break;
            case "End" or "PageDown":
                _cursor = LastEnabled(entries, level);
                break;
            case "ArrowRight" when at >= 0 && entries[at] is { IsSub: true, Disabled: false }:
                _openSubs.Add(at);
                _cursor = FirstEnabled(entries, at) is var first and >= 0 ? first : at;
                break;
            case "ArrowLeft" when level >= 0:
                CloseSubsFrom(entries, level);
                _cursor = level;
                break;
            default:
                if (e.Key.Length == 1 && e.Key != " ")
                {
                    TypeAhead(e.Key, entries, level, at);
                }

                break;
        }

        return Task.CompletedTask;
    }

    private void TypeAhead(string key, IReadOnlyList<UiMenuEntry> entries, int level, int at)
    {
        var siblings = Siblings(entries, level);
        var texts = new string?[siblings.Count];
        for (var i = 0; i < siblings.Count; i++)
        {
            texts[i] = siblings[i].Disabled ? null : siblings[i].Text;
        }

        var hit = _typeAhead.Next(key, IndexIn(siblings, at), texts, Clock);
        if (hit >= 0)
        {
            _cursor = siblings[hit].Ordinal;
        }
    }

    private void CloseSubsFrom(IReadOnlyList<UiMenuEntry> entries, int ordinal)
    {
        _openSubs.Remove(ordinal);
        foreach (var entry in entries)
        {
            if (entry.Parent == ordinal && entry.IsSub)
            {
                CloseSubsFrom(entries, entry.Ordinal);
            }
        }
    }

    private static List<UiMenuEntry> Siblings(IReadOnlyList<UiMenuEntry> entries, int level)
    {
        var siblings = new List<UiMenuEntry>();
        foreach (var entry in entries)
        {
            if (entry.Parent == level)
            {
                siblings.Add(entry);
            }
        }

        return siblings;
    }

    private static int IndexIn(List<UiMenuEntry> siblings, int ordinal) =>
        siblings.FindIndex(s => s.Ordinal == ordinal);

    // Wraps, and skips what cannot be picked — the menu pattern, unlike a tree's cursor, which stops at the ends.
    private static int Step(IReadOnlyList<UiMenuEntry> entries, int level, int at, int dir)
    {
        var siblings = Siblings(entries, level);
        var from = IndexIn(siblings, at);
        var index = from < 0 ? -1 : UiSelectNav.Step(from, dir, siblings.Count, i => siblings[i].Disabled);

        // UiSelectNav stops at the ends; a menu wraps — and with no cursor yet, Down starts at the top and Up at
        // the bottom.
        if (index < 0 || index == from)
        {
            index = dir > 0
                ? UiSelectNav.FirstEnabled(siblings.Count, i => siblings[i].Disabled)
                : UiSelectNav.LastEnabled(siblings.Count, i => siblings[i].Disabled);
        }

        return index >= 0 ? siblings[index].Ordinal : at;
    }

    private static int FirstEnabled(IReadOnlyList<UiMenuEntry> entries, int level)
    {
        var siblings = Siblings(entries, level);
        var index = UiSelectNav.FirstEnabled(siblings.Count, i => siblings[i].Disabled);
        return index >= 0 ? siblings[index].Ordinal : -1;
    }

    private static int LastEnabled(IReadOnlyList<UiMenuEntry> entries, int level)
    {
        var siblings = Siblings(entries, level);
        var index = UiSelectNav.LastEnabled(siblings.Count, i => siblings[i].Disabled);
        return index >= 0 ? siblings[index].Ordinal : -1;
    }
}
