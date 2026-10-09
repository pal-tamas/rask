using System.Globalization;
using Rask.Core.Live;

namespace Rask;

/// <summary>
/// A menu of actions, with the keyboard cursor. Flux UI's <c>flux:menu</c>.
/// </summary>
/// <remarks>
/// <para>
/// It is the popup itself: a <c>[popover]</c> that says <c>role="menu"</c>, opened by the
/// <see cref="UiDropdown" /> or the <see cref="UiContext" /> it sits in. Its children are the rows —
/// <see cref="UiMenuItem" />, <see cref="UiMenuCheckbox" />, <see cref="UiMenuRadio" />,
/// <see cref="UiMenuSubmenu" />, <see cref="UiMenuSeparator" />, <see cref="UiMenuGroup" />.
/// </para>
/// <para>
/// The keyboard is Flux's, key for key. It opens with focus on the menu and no row chosen; ArrowDown or ArrowUp
/// puts the cursor on the first row — or on the row the pointer is resting on — and from there the arrows move
/// it a row at a time, stopping at the ends and stepping over what is disabled. Typing letters jumps to the row that starts with them. ArrowRight or
/// Enter on a submenu's row opens it with the cursor on its first row, ArrowLeft closes it. Enter or Space
/// picks, Tab leaves, Escape closes — and the row under the cursor is the one that has FOCUS, so a screen
/// reader follows it.
/// </para>
/// <para>
/// The pointer is Flux's too, and it is the runtime's hooks that make it so. A row is lit — <c>data-active</c>,
/// the one mark both the pointer and the keyboard write — the moment the pointer enters it
/// (<c>data-rask-menu-pointer</c>) and focus stays where it was; the arrows then count from the lit row. A
/// submenu opens under the pointer, stays while the pointer crosses to it (<c>data-rask-safe-area</c>) and after
/// it has left the menu, and closes when another row is entered. The page behind neither scrolls nor takes the
/// pointer (<c>data-rask-lock</c>) — except behind a menu the pointer opened, which locks nothing and takes
/// no focus.
/// </para>
/// <para>
/// A pick closes the menu. <see cref="KeepOpen" /> keeps it up, for a menu of checkboxes; a row can ask for the
/// same on its own.
/// </para>
/// </remarks>
public sealed partial class UiMenu : Component
{
    // Per instance: a menu outside a dropdown still needs an id of its own.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);
    private readonly HashSet<int> _openSubs = [];

    // Two places, as Flux keeps them: the row that is LIT — the pointer's or the keyboard's, whichever moved
    // last — and the row that has FOCUS, which only the keyboard and a press move (-1: the menu itself has it).
    private int _cursor = -1;
    private int _focus = -1;

    // The submenu the pointer opened by resting on its row: the press that follows leaves it open.
    private int _rested = -1;
    private UiMenuScope? _scope;
    private UiPopupHost? _host;
#pragma warning disable S3459 // a mutable struct whose default is its empty state; Next() fills it in place
    private UiTypeAhead _typeAhead;
#pragma warning restore S3459

    /// <summary>Keeps the menu open after any row in it is picked.</summary>
    public bool? KeepOpen { get; set; }

    /// <summary>
    ///     The menu's id, for one that sits outside the <see cref="UiContext" /> that opens it and is named by its
    ///     <see cref="UiContext.Target" />. Inside a dropdown or a context menu the id is theirs.
    /// </summary>
    public string? Id { get; set; }

    public string? Class { get; set; }

    // The clock the type-ahead's prefix expires by. Internal, so it is not a chain step; a test hands it its own.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    // The cursor and the open submenus are FIELDS, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        _host = Context.Get<UiPopupHost>();
        var id = _host?.PanelId ?? Id ?? "uimenu-" + _instance.ToString(CultureInfo.InvariantCulture);
        // The rows around the keyboard's are worked out against the list the last render left: the one on screen.
        var around = Around(_scope?.Entries);
        _scope = new UiMenuScope(id, _cursor, _openSubs, ToggleSubAsync, MoveTo) { Focus = _focus, Around = around, PointAt = PointAt };

        // The runtime lights the row under the pointer at once (rask-menu.ts); PointAt is this menu catching up.
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-menu"] = "", ["rask-menu-pointer"] = "" };
        if (_host?.Hover != true)
        {
            // The page behind neither scrolls nor takes the pointer, as under Flux's dropdown and context menu
            // (rask-lock.ts). Not under a menu the pointer opens: a page with no pointer would take it from the
            // trigger too, and Flux locks nothing there either.
            data["rask-lock"] = "";
        }

        if (KeepOpen == true)
        {
            // The runtime closes the popover on a pick unless the row, or something around it, says this.
            data["rask-keep-open"] = "";
        }

        if (_host?.Controlled is { } controlled)
        {
            // The runtime shows or hides the popover to match whenever this changes (rask-dom.ts).
            data["rask-popover-open"] = controlled ? "true" : "false";
        }

        if (_host?.Detail is { } detail)
        {
            data["detail"] = detail;
        }

        // `autofocus`: the popover focusing steps move focus to it as it opens, so the arrow keys reach the menu
        // straight from whatever opened it — with no row chosen yet, as Flux leaves it.
        // On its own it is a menu some Ui.Context names by id, so it sits where the pointer was.
        var style = _host?.Style ?? global::Rask.UiContext.PointerStyle(Ui.ContextPosition.BottomEnd, distance: null, (0, 0));

        return Div
            .Id(id)
            .Popover(Popover.Auto)
            .Role("menu")
            .TabIndex(-1)
            .Class(UiClass.Compose(UiMenuRow.Panel, Class))
            .Data(data)
            .Attributes(("autofocus", null), ("style", style))
            // The SOLE writer of the open state: the browser opens the popover and closes it on Escape, a click
            // outside, a pick and Tab, and this is where C# hears which.
            .OnToggle(OnToggleAsync)
            .OnPointerLeave(OnPointerLeave)
            .OnKeyDown(OnKey)[
            Context.Provide(new UiMenuLevel(_scope, -1))[Children ?? []]
        ];
    }

    private async Task OnToggleAsync(ToggleEvent e)
    {
        var open = string.Equals(e.NewState, "open", StringComparison.Ordinal);
        if (!open)
        {
            // Closed, it forgets: the next opening starts with no row chosen and every submenu shut. Not on
            // opening — ArrowDown on the trigger opens the menu AND asks for the first row, and that key arrives
            // before this event does.
            _cursor = _focus = _rested = -1;
            _openSubs.Clear();
        }

        if (_host is { } host)
        {
            await host.Toggled(open).ConfigureAwait(false);
        }
    }

    // A press focuses the row it lands on, so the keyboard continues from there.
    private void MoveTo(int ordinal) => _cursor = _focus = ordinal;

    private Task ToggleSubAsync(int ordinal)
    {
        if (_scope is not { } scope || ordinal < 0 || ordinal >= scope.Entries.Count)
        {
            return Task.CompletedTask;
        }

        // A tap has no hover, so it opens and the next one closes. A press on a row the pointer was already
        // resting on is not that second tap: the flyout it opened stays.
        var rested = _rested == ordinal && _openSubs.Contains(ordinal);
        if (!rested && !_openSubs.Remove(ordinal))
        {
            _openSubs.Add(ordinal);
        }
        else if (!rested)
        {
            CloseSubsFrom(scope.Entries, ordinal);
        }

        _cursor = _focus = ordinal;
        return Task.CompletedTask;
    }

    // ---- the pointer, as Flux's menu answers it ----------------------------------------------------

    // The pointer entered a row. It is lit from then on — the runtime lit it already — every submenu it is not
    // inside closes, and a submenu's own row opens its flyout: held in state, so the flyout is still there
    // when the pointer has left the menu. Focus is not the pointer's to move, unless the row it was on is gone.
    private void PointAt(int ordinal, PointerEvent e)
    {
        // A touch reports one enter for the tap that follows it, which is not hovering.
        if (_scope is not { } scope || ordinal < 0 || ordinal >= scope.Entries.Count
            || string.Equals(e.PointerType, "touch", StringComparison.Ordinal))
        {
            return;
        }

        var entries = scope.Entries;
        _cursor = ordinal;
        _rested = -1;
        _openSubs.RemoveWhere(open => open != ordinal && !Within(entries, ordinal, open));
        if (entries[ordinal] is { IsSub: true, Disabled: false })
        {
            _openSubs.Add(ordinal);
            _rested = ordinal;
        }

        _focus = StillShown(entries, _focus);
    }

    // The pointer left the menu, flyouts and all. What it lit goes dark; the row the keyboard is on stays lit,
    // and an open flyout stays open — on Flux it is there until another row is entered.
    private void OnPointerLeave(PointerEvent e)
    {
        if (_cursor != _focus)
        {
            _cursor = -1;
        }
    }

    // ---- the keyboard, as Flux's menu answers it ---------------------------------------------------

    private void OnKey(KeyboardEvent e)
    {
        // A modified key belongs to the browser or the app, not to the menu.
        if (_scope is not { } scope || scope.Entries.Count == 0 || e.CtrlKey || e.AltKey || e.MetaKey)
        {
            return;
        }

        var entries = scope.Entries;
        var lit = _cursor >= 0 && _cursor < entries.Count ? _cursor : -1;
        var onRow = _focus >= 0 && _focus < entries.Count;
        // The keys count from the lit row, wherever focus is — and from the focused one once the pointer has
        // lit a row and left, where Flux's keys do nothing at all.
        var at = lit >= 0 || !onRow ? lit : _focus;
        var level = at >= 0 ? entries[at].Parent : -1;
        var onSub = at >= 0 && entries[at] is { IsSub: true, Disabled: false };

        switch (e.Key)
        {
            // Focus is still on the menu: either arrow lands on the row the pointer lit, or starts at the top.
            case Keys.ArrowDown or Keys.ArrowUp when !onRow:
                Land(entries, at >= 0 ? at : FirstEnabled(entries, -1));
                break;
            case Keys.ArrowDown:
                Land(entries, Step(entries, level, at, +1));
                break;
            case Keys.ArrowUp:
                Land(entries, Step(entries, level, at, -1));
                break;
            case Keys.ArrowRight or Keys.Enter when onSub:
                _openSubs.Add(at);
                _cursor = _focus = FirstEnabled(entries, at) is var first and >= 0 ? first : at;
                break;
            // Space opens a submenu and leaves the cursor on its row.
            case " " when onSub:
                _openSubs.Add(at);
                _cursor = _focus = at;
                break;
            case Keys.ArrowLeft when level >= 0:
                CloseSubsFrom(entries, level);
                _cursor = _focus = level;
                break;
            default:
                if (e.Key.Length == 1 && !string.Equals(e.Key, " ", StringComparison.Ordinal))
                {
                    TypeAhead(e.Key, entries, level, at);
                }

                break;
        }
    }

    private void TypeAhead(string key, IReadOnlyList<UiMenuEntry> entries, int level, int at)
    {
        var siblings = Siblings(entries, level);
        var texts = new string?[siblings.Count];
        for (var i = 0; i < siblings.Count; i++)
        {
            texts[i] = siblings[i].Disabled ? null : siblings[i].Text;
        }

        var hit = _typeAhead.Next(key, siblings.FindIndex(s => s.Ordinal == at), texts, Clock);
        if (hit >= 0)
        {
            Land(entries, siblings[hit].Ordinal);
        }
    }

    // The keyboard arrives on a row: lit and focused, and every submenu it is not inside shut.
    private void Land(IReadOnlyList<UiMenuEntry> entries, int ordinal)
    {
        if (ordinal < 0)
        {
            return;
        }

        _cursor = _focus = ordinal;
        _rested = -1;
        _openSubs.RemoveWhere(open => !Within(entries, ordinal, open));
    }

    // The submenu rows around the keyboard's row, which Flux leaves lit while the arrows are in their flyout.
    private HashSet<int>? Around(IReadOnlyList<UiMenuEntry>? entries)
    {
        if (entries is null || _focus != _cursor || _focus < 0 || _focus >= entries.Count || entries[_focus].Parent < 0)
        {
            return null;
        }

        var around = new HashSet<int>();
        for (var at = entries[_focus].Parent; at >= 0 && at < entries.Count; at = entries[at].Parent)
        {
            around.Add(at);
        }

        return around;
    }

    // The row itself, or — when a submenu around it has closed — that submenu's row: where Flux puts focus
    // when the flyout it was in goes away.
    private int StillShown(IReadOnlyList<UiMenuEntry> entries, int row)
    {
        var shown = row;
        for (var at = row; at >= 0 && at < entries.Count && entries[at].Parent >= 0; at = entries[at].Parent)
        {
            if (!_openSubs.Contains(entries[at].Parent))
            {
                shown = entries[at].Parent;
            }
        }

        return shown;
    }

    private static bool Within(IReadOnlyList<UiMenuEntry> entries, int row, int submenu)
    {
        for (var at = entries[row].Parent; at >= 0 && at < entries.Count; at = entries[at].Parent)
        {
            if (at == submenu)
            {
                return true;
            }
        }

        return false;
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

    private static List<UiMenuEntry> Siblings(IReadOnlyList<UiMenuEntry> entries, int level) =>
        [.. entries.Where(entry => entry.Parent == level)];

    // Stops at the ends, as Flux's menu does, and steps over what cannot be picked.
    private static int Step(IReadOnlyList<UiMenuEntry> entries, int level, int at, int dir)
    {
        var siblings = Siblings(entries, level);
        var from = siblings.FindIndex(s => s.Ordinal == at);
        var index = from < 0 ? -1 : UiSelectNav.Step(from, dir, siblings.Count, i => siblings[i].Disabled);
        return index >= 0 ? siblings[index].Ordinal : at;
    }

    private static int FirstEnabled(IReadOnlyList<UiMenuEntry> entries, int level)
    {
        var siblings = Siblings(entries, level);
        var index = UiSelectNav.FirstEnabled(siblings.Count, i => siblings[i].Disabled);
        return index >= 0 ? siblings[index].Ordinal : -1;
    }
}
