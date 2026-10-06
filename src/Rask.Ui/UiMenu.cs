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
/// puts the cursor on the first row, and from there the arrows move it a row at a time, stopping at the ends
/// and stepping over what is disabled. Typing letters jumps to the row that starts with them. ArrowRight or
/// Enter on a submenu's row opens it with the cursor on its first row, ArrowLeft closes it. Enter or Space
/// picks, Tab leaves, Escape closes — and the row under the cursor is the one that has FOCUS, so a screen
/// reader follows it.
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

    private int _cursor = -1;
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
        _scope = new UiMenuScope(id, _cursor, _openSubs, ToggleSubAsync, MoveTo);

        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["ui-menu"] = "" };
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
            _cursor = -1;
            _openSubs.Clear();
        }

        if (_host is { } host)
        {
            await host.Toggled(open).ConfigureAwait(false);
        }
    }

    private void MoveTo(int ordinal) => _cursor = ordinal;

    private Task ToggleSubAsync(int ordinal)
    {
        if (_scope is not { } scope || ordinal < 0 || ordinal >= scope.Entries.Count)
        {
            return Task.CompletedTask;
        }

        if (!_openSubs.Remove(ordinal))
        {
            _openSubs.Add(ordinal);
        }
        else
        {
            CloseSubsFrom(scope.Entries, ordinal);
        }

        _cursor = ordinal;
        return Task.CompletedTask;
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
        var at = _cursor >= 0 && _cursor < entries.Count ? _cursor : -1;
        var level = at >= 0 ? entries[at].Parent : -1;
        var onSub = at >= 0 && entries[at] is { IsSub: true, Disabled: false };

        switch (e.Key)
        {
            // With no row chosen yet, either arrow starts at the top.
            case Keys.ArrowDown or Keys.ArrowUp when at < 0:
                _cursor = FirstEnabled(entries, -1);
                break;
            case Keys.ArrowDown:
                _cursor = Step(entries, level, at, +1);
                break;
            case Keys.ArrowUp:
                _cursor = Step(entries, level, at, -1);
                break;
            case Keys.ArrowRight or Keys.Enter when onSub:
                _openSubs.Add(at);
                _cursor = FirstEnabled(entries, at) is var first and >= 0 ? first : at;
                break;
            // Space opens a submenu and leaves the cursor on its row.
            case " " when onSub:
                _openSubs.Add(at);
                break;
            case Keys.ArrowLeft when level >= 0:
                CloseSubsFrom(entries, level);
                _cursor = level;
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
