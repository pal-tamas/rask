using System.Text;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     Flux's <c>flux:button</c>. It IS the <c>&lt;button&gt;</c> — or, given <see cref="Href" />, the <c>&lt;a&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Button["Save"]</c> is Flux's default, the outline. <see cref="Variant" />, <see cref="Size" />
///     and <see cref="Color" /> are steps — <c>Ui.Button.Primary.Sm.Blue["Save"]</c> — and an icon is a prop,
///     which is what lets the button pad itself around it: <c>Ui.Button.Icon(Ui.IconName.ArrowDownTray)["Export"]</c>.
///     With an icon and no children it is a square.
///     </para>
///     <para>
///     A <see cref="UiElement" />: its children are its label, and every element step (<c>Id</c>, <c>Class</c>,
///     <c>Aria</c>, <c>OnClick</c> and the rest of the events) is <see cref="Element" />'s. A square shows only
///     a glyph, so name it: <c>.Aria("label", "Close")</c>, or a <see cref="Tooltip" />, which names it too.
///     </para>
/// </remarks>
public sealed partial class UiButton : UiElement, IUiHost
{
    private static readonly UiPartMarker Marker = new("ui-button");

    // Flux marks a button twice unless it is ghost or subtle: the second is what a group reaches its buttons by.
    private static readonly UiPartMarker Grouped = Marker.And("ui-group-target");

    // `data-ui-loading` is Flux's `data-flux-loading`; `data-loading` is the runtime's, and what the spinner shows on.
    private static readonly UiPartMarker Waiting = Marker.And("ui-loading").And("loading");
    private static readonly UiPartMarker GroupedWaiting = Grouped.And("ui-loading").And("loading");

    // The runtime reads `data-rask-loading="off"` and leaves the button alone.
    private static readonly UiPartMarker Unmarked = Marker.And("rask-loading", "off");
    private static readonly UiPartMarker GroupedUnmarked = Grouped.And("rask-loading", "off");

    private static readonly Dictionary<string, string?> IndicatorMark = new(StringComparer.Ordinal)
    {
        ["data-ui-loading-indicator"] = null,
    };

    /// <summary>How it is drawn. <see cref="Ui.ButtonVariant.Outline" /> when unset.</summary>
    public Ui.ButtonVariant? Variant { get; set; }

    /// <summary>How large it is. <see cref="Ui.ButtonSize.Base" />, 40px tall, when unset.</summary>
    public Ui.ButtonSize? Size { get; set; }

    /// <summary>
    ///     A Tailwind hue to draw it in. The seventeen hues recolour every variant but
    ///     <see cref="Ui.ButtonVariant.Danger" />; a gray changes only <see cref="Ui.ButtonVariant.Primary" />.
    /// </summary>
    public Ui.Color? Color { get; set; }

    /// <summary>What it does when pressed. <see cref="Ui.ButtonType.Button" /> — nothing on its own — when unset.</summary>
    /// <remarks>
    ///     Set <see cref="Ui.ButtonType.Submit" /> on a form's submit button. Ignored when it is not a
    ///     <c>&lt;button&gt;</c>.
    /// </remarks>
    public Ui.ButtonType? Type { get; set; }

    /// <summary>The element to render. <see cref="Href" /> makes it an <c>&lt;a&gt;</c> without this.</summary>
    public Ui.ButtonAs? As { get; set; }

    /// <summary>The icon before the label — or, with no label, the whole button.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>
    ///     Which drawing of the icons. Unset, it is Flux's choice: micro beside a label, mini alone in a square.
    /// </summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>The icon after the label.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>
    ///     As wide as it is tall, with no padding. Automatic for a button with no children;
    ///     <see langword="false" /> turns that off.
    /// </summary>
    public bool? Square { get; set; }

    /// <summary>Where the content sits in a button wider than it. Centred when unset.</summary>
    public Ui.Align? Align { get; set; }

    /// <summary>
    ///     The sides to pull outwards by the button's invisible padding, so a ghost or subtle button lines up
    ///     with the text around it.
    /// </summary>
    public Ui.Inset? Inset { get; set; }

    /// <summary>
    ///     Whether it shows that it is waiting. Unset is AUTOMATIC: the runtime marks a button while its own
    ///     <c>OnClick</c> — or its form's submit — is still running.
    /// </summary>
    /// <remarks>
    ///     After 200 ms without an answer the runtime writes <c>data-loading</c> and <c>aria-busy</c>: the label
    ///     fades out at its own width, a spinner takes its place and a second press is dropped. It is never
    ///     <c>disabled</c>, which would throw keyboard focus off the control mid-press.
    ///     <see langword="false" /> opts out — a stepper whose presses are meant to queue.
    ///     <see langword="true" /> shows it from C#, for work that outlives the handler, by <c>data-loading</c>
    ///     alone: Flux's loading button carries no ARIA. Ignored when it is not a <c>&lt;button&gt;</c>.
    /// </remarks>
    public bool? Loading { get; set; }

    /// <summary>
    ///     A hint shown while the button is hovered or focused: the button is wrapped in a
    ///     <see cref="UiTooltip" />, as Flux wraps it. It names a button that has no label.
    /// </summary>
    public string? Tooltip { get; set; }

    /// <summary>Which side of the button the tooltip opens on. Above when unset.</summary>
    public Ui.TooltipPosition? TooltipPosition { get; set; }

    /// <summary>A keyboard shortcut shown at the end of the <see cref="Tooltip" />: <c>"⌘S"</c>.</summary>
    public string? TooltipKbd { get; set; }

    /// <summary>A keyboard shortcut shown inside the button, after its label: <c>"esc"</c>.</summary>
    public string? Kbd { get; set; }

    /// <summary>
    ///     Set by a <see cref="UiTooltip" /> around this button: inside a group the button is then not the
    ///     group's own child, and fuses by where its tooltip stands.
    /// </summary>
    internal bool InTooltip { get; set; }

    /// <summary>
    ///     Where it goes. Set this and it renders an <c>&lt;a&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     A generated route — <c>Routes.Orders()</c> — navigates INSIDE the app, as a <c>NavLink</c> does: the
    ///     anchor carries <c>data-rask-nav</c> and the deploy's path base. A plain string is a link the browser
    ///     follows itself. Sanitised as Core's <c>A</c> sanitises its own. An anchor takes no <c>type</c> and
    ///     cannot be <see cref="Disabled" />: a link that should not be followed should not be rendered.
    /// </remarks>
    public RouteUrl? Href { get; set; }

    /// <summary>Whether it is disabled — by ATTRIBUTE, so the browser refuses the press. A <c>&lt;button&gt;</c> only.</summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     The action to invoke on the element named by <see cref="CommandFor" />, as HTML's invoker API:
    ///     a dialog or a popover opened with no script. A <c>&lt;button&gt;</c> only.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>The id of the element <see cref="Command" /> acts on.</summary>
    public string? CommandFor { get; set; }

    /// <inheritdoc />
    /// <remarks>None when it has a <see cref="Tooltip" />: it then renders as the tooltip, with its element inside.</remarks>
    protected override string? TagName => Tooltip is null ? ElementTag : null;

    /// <inheritdoc />
    /// <remarks>The label is part of what a button with a tooltip renders, so that render is never reused.</remarks>
    protected override bool BypassRenderCache => Tooltip is not null;

    private string ElementTag => (Link, As) switch
    {
        (not null, _) or (_, Ui.ButtonAs.A) => "a",
        (_, Ui.ButtonAs.Div) => "div",
        _ => "button",
    };

    // A null string reaching Href converts to a RouteUrl with no path, and that is a button, not a link.
    private RouteUrl? Link => Href is { Path: not null } ? Href : null;

    private bool IsButton => Link is null && As is null or Ui.ButtonAs.Button;

    private Ui.ButtonSize Sized => Size ?? Ui.ButtonSize.Base;

    private bool IsSquare => Square ?? Children is null;

    // Flux's rule — a button bound to an action, or a form's submit — and any button whose Loading the
    // caller set, either way: a flag that flips between renders must not change what the button is made of.
    private bool Loads => IsButton && (Loading is not null || OnClick.HasValue || Type == Ui.ButtonType.Submit);

    private bool HasParts => Icon is not null || IconTrailing is not null || Loads || Kbd is not null;

    /// <inheritdoc />
    protected override string? ResolveClass()
    {
        var variant = Variant ?? Ui.ButtonVariant.Outline;
        var chromatic = Color is { } color && UiButtonClasses.IsChromatic(color);
        var hued = chromatic && variant is not (Ui.ButtonVariant.Primary or Ui.ButtonVariant.Danger);

        return UiClass.Compose(
            UiButtonClasses.Base,
            UiButtonClasses.Size(Sized, Shape),
            UiButtonClasses.Align(Align),
            UiButtonClasses.Variant(variant, hued),
            UiButtonClasses.Shadow(variant, Sized),
            hued ? UiButtonClasses.Hue(Color!.Value) : null,
            variant == Ui.ButtonVariant.Primary && Color is { } accent ? UiButtonClasses.Accent(accent) : null,
            variant is Ui.ButtonVariant.Ghost or Ui.ButtonVariant.Subtle ? null : UiButtonClasses.Grouped(Sized),
            variant is Ui.ButtonVariant.Ghost or Ui.ButtonVariant.Subtle || !(InTooltip || Tooltip is not null)
                ? null
                : UiButtonClasses.GroupedInTooltip(Sized),
            Inset is { } inset ? UiButtonClasses.Inset(inset, Sized) : null,
            Class);
    }

    private UiButtonClasses.Shape Shape => (IsSquare, Icon is not null, IconTrailing is not null) switch
    {
        (true, _, _) => UiButtonClasses.Shape.Square,
        (_, true, true) => UiButtonClasses.Shape.IconBoth,
        (_, true, false) => UiButtonClasses.Shape.IconLeading,
        (_, false, true) => UiButtonClasses.Shape.IconTrailing,
        _ => UiButtonClasses.Shape.Text,
    };

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?>? ResolveData()
    {
        var grouped = Variant is not (Ui.ButtonVariant.Ghost or Ui.ButtonVariant.Subtle);
        var marker = (IsButton ? Loading : null) switch
        {
            true => grouped ? GroupedWaiting : Waiting,
            false => grouped ? GroupedUnmarked : Unmarked,
            _ => grouped ? Grouped : Marker,
        };

        return marker.With(Data);
    }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        if (Link is { } href)
        {
            WriteLink(sb, href);
        }
        else if (IsButton)
        {
            WriteButton(sb);
        }
    }

    private static void WriteLink(StringBuilder sb, RouteUrl href)
    {
        // A generated route carries its page type; a string converted to a RouteUrl does not. Only the first
        // is this app's to route, so only it is intercepted and prefixed with the deploy's PathBase (#975).
        var inApp = href.PageType is not null;
        AppendUrlAttr(sb, "href", inApp ? LiveOptions.PathBase + href.ToString() : href.ToString());

        if (inApp)
        {
            // The runtime's click interception selects on this. Without it the anchor is a full document load.
            // It leaves a `target="_blank"` the call site wrote alone.
            AppendAttr(sb, "data-rask-nav", null);
        }
    }

    private void WriteButton(StringBuilder sb)
    {
        AppendAttr(sb, "type", Type == Ui.ButtonType.Submit ? "submit" : "button");

        if (Disabled == true)
        {
            AppendAttr(sb, "disabled", null);
        }

        if (Command is { } command)
        {
            AppendAttr(sb, "command", command);
        }

        if (CommandFor is { } commandFor)
        {
            AppendAttr(sb, "commandfor", commandFor);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The serializer walks an indexer's array as it stands and never asks <see cref="RenderChildren" />.
    ///     A button with parts of its own — an icon, the spinner, a shortcut — hands it something that is not
    ///     an array, so it does ask. A plain <c>Ui.Button["Save"]</c> is left on the fast path.
    /// </remarks>
    protected override IDisposable? EnterChildrenScope()
    {
        if (HasParts && Children is Component?[] label)
        {
            Children = new ArraySegment<Component?>(label);
        }

        return base.EnterChildrenScope();
    }

    /// <inheritdoc />
    protected override IEnumerable<Component?> RenderChildren() => HasParts ? Parts() : base.RenderChildren();

    /// <inheritdoc />
    /// <remarks>
    ///     Reached only with a <see cref="Tooltip" />. Flux wraps such a button in its tooltip, so this renders
    ///     the tooltip around the button's element, which writes this component's own attributes: the id, the
    ///     classes and the handlers stay on the <c>&lt;button&gt;</c>, and the tooltip names or describes it.
    /// </remarks>
    protected override Component? Render() =>
        Ui.Tooltip.Content(Tooltip).Kbd(TooltipKbd).Position(TooltipPosition).Class("inline-flex")[
            HostedElement.Tag(ElementTag).Owner(this)[HasParts ? Parts() : Words ?? []]
        ];

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);

    // What the call site put in the indexer, whichever way EnterChildrenScope left it.
    private IEnumerable<Component?>? Words => Children is ArraySegment<Component?> segment ? segment.Array : Children;

    private Component?[] Parts()
    {
        var loads = Loads;
        return
        [
            loads ? Indicator() : null,
            Icon is { } icon ? Glyph(icon, loads) : null,
            Label(loads),
            Kbd is { } kbd ? Div.Class("text-xs text-zinc-400")[kbd] : null,
            IconTrailing is { } trailing ? Glyph(trailing, loads) : null,
        ];
    }

    // Over the whole button and invisible until the runtime — or Loading(true) — marks it.
    private static Component Indicator() =>
        Div.Class("absolute inset-0 flex items-center justify-center opacity-0 transition-opacity [[data-loading]>&]:opacity-100")
            .Attributes(IndicatorMark)[
            Ui.Icon.Name(Ui.IconName.Loading).Micro
        ];

    // 20px alone in a square, 16px beside a label — and in the smallest button either way.
    private UiIcon Glyph(Ui.IconName name, bool loads)
    {
        var large = IsSquare && Sized != Ui.ButtonSize.Xs;
        var variant = IconVariant ?? (large ? Ui.IconVariant.Mini : Ui.IconVariant.Micro);

        return Ui.Icon.Name(name).Variant(variant).Class((large, loads) switch
        {
            (true, true) => "size-5 transition-opacity [[data-loading]>&]:opacity-0",
            (true, false) => "size-5",
            (false, true) => "size-4 transition-opacity [[data-loading]>&]:opacity-0",
            _ => "size-4",
        });
    }

    private Component? Label(bool loads)
    {
        var label = Words;
        if (label is null)
        {
            return null;
        }

        return loads ? Span.Class("transition-opacity [[data-loading]>&]:opacity-0")[label] : Span[label];
    }
}
