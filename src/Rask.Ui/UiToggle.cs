using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     Flux's <c>flux:toggle</c>: a setting turned on or off from a compact, button-shaped control — an
///     icon that fills in when it is on, with or without a short label.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Fast mode")</c>. It is a <c>&lt;button role="switch"&gt;</c>,
///     so Space and Enter turn it and a screen reader says "on" or "off". An icon-only toggle takes its
///     name from <see cref="Tooltip" />.
///     </para>
///     <para>
///     <c>.Bind(() =&gt; settings.FastMode)</c> two-way binds: the member is written with the next action, at once
///     with <see cref="Live" />, and at once when the page listens with <see cref="OnChange" /> — Flux's
///     <c>wire:model</c> beside <c>wire:change</c>, one request that saves. Unbound and listened to with
///     <see cref="OnChange" /> it reports each turn, and a page that hands it a different
///     <see cref="Checked" /> has the last word. With neither it is turned in the browser, starting from
///     <see cref="Checked" />, and nothing is sent to the page (<c>data-rask-switch</c>).
///     While a change is being answered the runtime marks it <c>data-loading</c>: what it shows gives way to
///     a spinner and it takes no further press.
///     </para>
/// </remarks>
public sealed partial class UiToggle : Component, IDeferredBind
{
    // Whatever it holds — an icon of the kit's or of the app's, a label — steps aside for the spinner.
    private const string Frame =
        "group relative [:where(&)]:inline-flex items-center justify-center cursor-auto text-start font-medium whitespace-nowrap transition"
        + " outline-offset-2 *:transition-opacity *:duration-150 data-loading:*:opacity-0"
        + " disabled:opacity-50 disabled:cursor-default disabled:pointer-events-none disabled:shadow-none"
        + " data-loading:pointer-events-none";

    private const string Ink =
        "text-zinc-600/85 data-checked:text-zinc-800 dark:text-zinc-300/95 dark:data-checked:text-white";

    private const string IconInk = "text-zinc-500/85 dark:text-zinc-300/80";

    private const string SubtleIconInk = "text-zinc-400/90 dark:text-zinc-500/90 group-data-checked:text-inherit";

    private const string Spinner =
        "absolute inset-0 flex items-center justify-center opacity-0 group-data-loading:opacity-100!";

    private const string ShownOn = "hidden group-data-checked:block";

    private const string ShownOff = "group-data-checked:hidden";

    private static readonly Dictionary<string, string?> Off = new(StringComparer.Ordinal)
    {
        [UiOptionLook.ControlMark] = null,
        ["ui-toggle"] = null,
    };

    private static readonly Dictionary<string, string?> On = new(Off, StringComparer.Ordinal) { ["checked"] = null };

    // Its own to keep: the runtime turns it in the browser (rask-switch.ts), and the page hears nothing.
    private static readonly Dictionary<string, string?> OwnOff = new(Off, StringComparer.Ordinal) { ["rask-switch"] = null };

    private static readonly Dictionary<string, string?> OwnOn = new(On, StringComparer.Ordinal) { ["rask-switch"] = null };

    private static readonly Dictionary<string, string?> LoadingMark = new(StringComparer.Ordinal) { ["ui-loading-indicator"] = null };

    private bool _own;
    private bool? _given;
    private bool _seeded;

    /// <summary>
    ///     The icon: a Heroicon by name — its outline when off, its solid drawing when on — or icons of your
    ///     own, Flux's <c>icon</c> slot, each shown for its state with <c>group-data-checked:</c>.
    /// </summary>
    public UiInputIcon? Icon { get; set; }

    /// <summary>The icon shown when on, in place of <see cref="Icon" />.</summary>
    public Ui.IconName? OnIcon { get; set; }

    /// <summary>The icon shown when off, in place of <see cref="Icon" />.</summary>
    public Ui.IconName? OffIcon { get; set; }

    /// <summary>The label, in both states. Children take its place.</summary>
    public string? Label { get; set; }

    /// <summary>The label shown when on, in place of <see cref="Label" />.</summary>
    public string? OnLabel { get; set; }

    /// <summary>The label shown when off, in place of <see cref="Label" />.</summary>
    public string? OffLabel { get; set; }

    /// <summary>How large it is. <see cref="Ui.ToggleSize.Base" /> when unset.</summary>
    public Ui.ToggleSize? Size { get; set; }

    /// <summary>How it is drawn. <see cref="Ui.ToggleVariant.Outline" /> when unset.</summary>
    public Ui.ToggleVariant? Variant { get; set; }

    /// <summary>The colour of the icon when on. The accent colour when unset; off, it stays neutral.</summary>
    public Ui.Color? Color { get; set; }

    /// <summary>A tooltip, which is also the accessible name of a toggle that shows only an icon.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Whether it is on, for a toggle that is not bound: where it starts, and — with <see cref="OnChange" /> — what a page that keeps the state says.</summary>
    public bool? Checked { get; set; }

    /// <summary>Takes it out of use: dimmed, and out of the tab order.</summary>
    public bool? Disabled { get; set; }

    /// <summary>The <c>id</c> of the toggle.</summary>
    public string? Id { get; set; }

    /// <summary>Classes for the call site, added to the toggle's own.</summary>
    public string? Class { get; set; }

    /// <summary>Two-way binds it to a <c>bool</c>: <c>.Bind(() =&gt; settings.FastMode)</c>.</summary>
    public Expression<Func<bool>>? Bind { get; set; }

    /// <summary>Called with the new state each time it is turned — after the write, when it is bound.</summary>
    public Callback<bool> OnChange { get; set; }

    /// <summary>
    ///     Writes the model as soon as it is turned: <c>Ui.Toggle.Bind(() =&gt; m.FastMode).Live()</c>. Flux's
    ///     <c>wire:model.live</c>. Without it the turn is written with the next action, and travels with that —
    ///     Flux's <c>wire:model</c>.
    /// </summary>
    public bool? Live { get; set; }

    // A page that listens to the change is answered now, with the member already written.
    bool? IDeferredBind.Live => OnChange.HasValue ? true : Live;

    // Its own state is a field, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    private bool Labelled => Label is not null || OnLabel is not null || OffLabel is not null || HasChildren;

    private bool HasChildren => Children?.Any(child => child is not null) == true;

    private bool Small => Size == Ui.ToggleSize.Xs || Labelled;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var on = Read().On;

        // A press only changes what it draws unless someone is told: bound, or listened to. Otherwise it is
        // the browser's, as Flux's is, and not one frame is sent.
        var heard = Bind is not null || OnChange.HasValue;
        var button = Button
            .Id(Id)
            .Type(ButtonType.Button)
            .Role("switch")
            .Aria("checked", on ? "true" : "false")
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(Frame, Shape(), Surface(), Class))
            .Data((on, heard) switch { (true, true) => On, (false, true) => Off, (true, false) => OwnOn, _ => OwnOff });

        // Bound and waiting, the turn is held for the next action: the press only draws it (UiDraws).
        var draws = UiDraws.Marks(("click", UiDraws.Waits(this, Bind is not null)));
        var toggle = (heard ? button.Attributes(draws).OnClick(TurnAsync) : button)[
            Div.Class(Spinner).Data(LoadingMark).Aria("hidden", "true")[
                Ui.Icon.Name(Ui.IconName.Loading).Class(Small ? "size-4" : "size-5")
            ],
            Icons(),
            Labels()
        ];

        // Flux wraps a toggle in its tooltip, which names it: the trigger has no words of its own.
        return Tooltip is { } tip ? Ui.Tooltip.Content(tip)[toggle] : toggle;
    }

    /// <summary>Turns it: what a click, Space or Enter does.</summary>
    internal Task TurnAsync()
    {
        var (accessor, context, on) = Read();
        var next = !on;

        // The reader's change, held or written: a form that asks before it is left counts it (ReaderEdits).
        ReaderEdits.Note(this);
        if (accessor is null)
        {
            _own = next;
            return OnChange.Invoke(next).AsTask();
        }

        return PendingBinds.Commit(this, accessor.Field, context, next, Write, SettleAsync);

        bool Write()
        {
            accessor.Setter(next);
            return true;
        }

        async Task SettleAsync()
        {
            await BindingHelpers.NotifyAndValidateField(context, accessor.Field).ConfigureAwait(false);
            await OnChange.Invoke(next).ConfigureAwait(false);
        }
    }

    // Bound, it is the model's — or the turn the session still holds for it. Unbound, its own, from Checked.
    private (ExpressionAccessor.Accessor? Accessor, EditContext? Context, bool On) Read()
    {
        if (Bind is not { } bind)
        {
            if (!_seeded || _given != Checked)
            {
                (_seeded, _given, _own) = (true, Checked, Checked == true);
            }

            return (null, null, _own);
        }

        var accessor = ExpressionAccessor.Parse(bind);
        var context = BindingHelpers.ResolveBindingContext(accessor.Target);
        var shown = PendingBinds.TryPeek(this, accessor.Field, out var chosen) ? chosen : accessor.Getter();
        return (accessor, context, shown is true);
    }

    private Component?[] Icons()
    {
        if (Icon is { Content: { } own })
        {
            return [own];
        }

        var size = Small ? "size-4" : "size-5";
        var ink = UiClass.Compose(size, Variant == Ui.ToggleVariant.Subtle ? SubtleIconInk : IconInk, Accent());
        return
        [
            (OnIcon ?? Icon?.Name) is { } on ? Ui.Icon.Name(on).Variant(Ui.IconVariant.Solid).Class(UiClass.Compose(ShownOn, ink)) : null,
            (OffIcon ?? Icon?.Name) is { } off ? Ui.Icon.Name(off).Class(UiClass.Compose(ShownOff, ink)) : null,
        ];
    }

    private Component?[] Labels()
    {
        if (HasChildren && Children is { } children)
        {
            return [Span[children]];
        }

        var off = OffLabel ?? Label;
        var on = OnLabel ?? Label;
        return
        [
            off is null ? null : Span.Class(ShownOff)[off],
            on is null ? null : Span.Class(ShownOn)[on],
        ];
    }

    // Square around an icon alone; with a label, a pill that is tighter on the icon's side.
    private string Shape()
    {
        var iconFirst = Icon is not null || OnIcon is not null || OffIcon is not null;
        return (Size ?? Ui.ToggleSize.Base, Labelled, iconFirst) switch
        {
            (Ui.ToggleSize.Sm, false, _) => "[:where(&)]:size-8 [:where(&)]:gap-2 [:where(&)]:text-sm [:where(&)]:rounded-md",
            (Ui.ToggleSize.Sm, true, true) => "h-8 [:where(&)]:ps-2 [:where(&)]:pe-3 [:where(&)]:gap-2 [:where(&)]:text-sm [:where(&)]:rounded-md",
            (Ui.ToggleSize.Sm, true, false) => "h-8 [:where(&)]:px-3 [:where(&)]:gap-2 [:where(&)]:text-sm [:where(&)]:rounded-md",
            (Ui.ToggleSize.Xs, false, _) => "[:where(&)]:size-6 [:where(&)]:gap-1 [:where(&)]:text-xs [:where(&)]:rounded-md",
            (Ui.ToggleSize.Xs, true, true) => "h-6 [:where(&)]:ps-1.5 [:where(&)]:pe-2 [:where(&)]:gap-1 [:where(&)]:text-xs [:where(&)]:rounded-md",
            (Ui.ToggleSize.Xs, true, false) => "h-6 [:where(&)]:px-2 [:where(&)]:gap-1 [:where(&)]:text-xs [:where(&)]:rounded-md",
            (_, false, _) => "[:where(&)]:size-10 [:where(&)]:gap-2 [:where(&)]:text-sm [:where(&)]:rounded-lg",
            (_, true, true) => "h-10 [:where(&)]:ps-3 [:where(&)]:pe-4 [:where(&)]:gap-2 [:where(&)]:text-sm [:where(&)]:rounded-lg",
            (_, true, false) => "h-10 [:where(&)]:px-4 [:where(&)]:gap-2 [:where(&)]:text-sm [:where(&)]:rounded-lg",
        };
    }

    private string Surface() => (Variant ?? Ui.ToggleVariant.Outline, Size) switch
    {
        (Ui.ToggleVariant.Filled, _) =>
            Ink + " bg-zinc-800/5 hover:bg-zinc-800/10 dark:bg-white/10 dark:hover:bg-white/20",
        (Ui.ToggleVariant.Ghost, _) =>
            Ink + " bg-transparent hover:bg-zinc-800/5 dark:hover:bg-white/15",
        (Ui.ToggleVariant.Subtle, _) =>
            "text-zinc-500/85 hover:text-zinc-500 data-checked:text-zinc-500 dark:text-zinc-400/80 dark:hover:text-zinc-300"
            + " dark:data-checked:text-zinc-400 bg-transparent hover:bg-zinc-800/5 dark:hover:bg-white/15",
        (_, Ui.ToggleSize.Xs) =>
            Ink + " border border-zinc-200 border-b-zinc-300/80 bg-white shadow-none hover:bg-zinc-50 hover:border-b-zinc-200"
            + " dark:border-zinc-600 dark:bg-zinc-700 dark:hover:border-b-zinc-600 dark:hover:bg-zinc-600/75",
        _ =>
            Ink + " border border-zinc-200 border-b-zinc-300/80 bg-white shadow-xs hover:bg-zinc-50 hover:border-b-zinc-200"
            + " dark:border-zinc-600 dark:bg-zinc-700 dark:hover:border-b-zinc-600 dark:hover:bg-zinc-600/75",
    };

    // On, the icon is the accent's or the chosen hue's; a subtle toggle with no hue keeps its own ink.
    private string Accent() => Color switch
    {
        Ui.Color.Red => "group-data-checked:text-red-600 dark:group-data-checked:text-red-400",
        Ui.Color.Orange => "group-data-checked:text-orange-600 dark:group-data-checked:text-orange-400",
        Ui.Color.Amber => "group-data-checked:text-amber-600 dark:group-data-checked:text-amber-400",
        Ui.Color.Yellow => "group-data-checked:text-yellow-600 dark:group-data-checked:text-yellow-400",
        Ui.Color.Lime => "group-data-checked:text-lime-600 dark:group-data-checked:text-lime-400",
        Ui.Color.Green => "group-data-checked:text-green-600 dark:group-data-checked:text-green-400",
        Ui.Color.Emerald => "group-data-checked:text-emerald-600 dark:group-data-checked:text-emerald-400",
        Ui.Color.Teal => "group-data-checked:text-teal-600 dark:group-data-checked:text-teal-400",
        Ui.Color.Cyan => "group-data-checked:text-cyan-600 dark:group-data-checked:text-cyan-400",
        Ui.Color.Sky => "group-data-checked:text-sky-600 dark:group-data-checked:text-sky-400",
        Ui.Color.Blue => "group-data-checked:text-blue-600 dark:group-data-checked:text-blue-400",
        Ui.Color.Indigo => "group-data-checked:text-indigo-600 dark:group-data-checked:text-indigo-400",
        Ui.Color.Violet => "group-data-checked:text-violet-600 dark:group-data-checked:text-violet-400",
        Ui.Color.Purple => "group-data-checked:text-purple-600 dark:group-data-checked:text-purple-400",
        Ui.Color.Fuchsia => "group-data-checked:text-fuchsia-600 dark:group-data-checked:text-fuchsia-400",
        Ui.Color.Pink => "group-data-checked:text-pink-600 dark:group-data-checked:text-pink-400",
        Ui.Color.Rose => "group-data-checked:text-rose-600 dark:group-data-checked:text-rose-400",
        _ => Variant == Ui.ToggleVariant.Subtle ? "" : "group-data-checked:text-accent-content",
    };
}
