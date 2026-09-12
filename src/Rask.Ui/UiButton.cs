using System.Text;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>
/// A button. It IS the <c>&lt;button&gt;</c> — or, given <see cref="Href" />, the <c>&lt;a&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>btn</c>, which carries the whole of what this component used to hand-roll: the touch
/// target, the focus ring, the disabled treatment, the hover transition and the four weights. Colour
/// (<see cref="Tone" />), fill (<see cref="Variant" />) and <see cref="Size" /> are three independent
/// axes and compose, so an outlined error button needs no member of its own.
/// </para>
/// <para>
/// A <see cref="UiElement" />, so what it shows is its CHILDREN — <c>UiButton["Save"]</c>, or
/// <c>UiButton[UiIcon.Name(UiIconName.Check), "Save"]</c> — and every element step (<c>Id</c>,
/// <c>Data</c>, <c>Role</c>, <c>TabIndex</c>, <c>Aria</c>, <c>OnClick</c> and the rest of the events) is
/// <see cref="Element" />'s, with nothing mirrored here to fall out of step. The kit sizes an icon placed
/// in a button from its stylesheet, so a bare <c>UiIcon.Name(…)</c> is the right size without a class.
/// </para>
/// <para>
/// A square or a circle holds one glyph, so its name cannot be visible text. Give it
/// <see cref="AccessibleLabel" />: a button whose only content is a decorative icon is announced as
/// "button" and nothing more.
/// </para>
/// </remarks>
public sealed partial class UiButton : UiElement
{
    /// <summary>The button's colour. Omitted, it is the theme's plain button.</summary>
    public UiTone? Tone { get; set; }

    /// <summary>How it is filled. <see cref="UiVariant.Ghost" /> is the quiet action.</summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>Fills the width of its container, which is what a button in a phone-width form wants.</summary>
    public bool? Block { get; set; }

    /// <summary>Wider than its content needs, without filling the container as <see cref="Block" /> does.</summary>
    public bool? Wide { get; set; }

    /// <summary>
    ///     Draws it as a square sized for one glyph. Pair it with <see cref="AccessibleLabel" />, since the
    ///     glyph is all a sighted user sees and a screen reader needs words.
    /// </summary>
    public bool? Square { get; set; }

    /// <summary>Draws it as a circle sized for one glyph, as <see cref="Square" />.</summary>
    public bool? Circle { get; set; }

    /// <summary>
    ///     Draws it as though it were being pressed. For a button that toggles something, where the
    ///     pressed look IS the state — a filter that is on, a panel that is showing.
    /// </summary>
    public bool? Active { get; set; }

    /// <summary>
    ///     The name a screen reader announces, for a button whose content does not say what it does — an
    ///     icon-only square or circle, most of all. Written as <c>aria-label</c>.
    /// </summary>
    /// <remarks>
    ///     A <c>label</c> set through <c>Aria</c> wins over this one: a caller naming it explicitly knows
    ///     better. Leave it off a button with visible text — that text is already its name, and a different
    ///     <c>aria-label</c> would make what a sighted user reads and what a screen reader says disagree.
    /// </remarks>
    public string? AccessibleLabel { get; set; }

    /// <summary>
    ///     The action to invoke on the element named by <see cref="CommandFor" />, as HTML's invoker API.
    /// </summary>
    /// <remarks>
    ///     <c>command</c>/<c>commandfor</c> open and close a dialog or a popover with NO script and no
    ///     handler on either side — the platform does it. Ignored when <see cref="Href" /> is set: only a
    ///     button is an invoker.
    /// </remarks>
    public string? Command { get; set; }

    /// <summary>The id of the element <see cref="Command" /> acts on.</summary>
    public string? CommandFor { get; set; }

    /// <summary>
    ///     Where it goes. Set this and it renders an <c>&lt;a&gt;</c> rather than a <c>&lt;button&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A link that looks like a button is an ordinary thing to want — a call to action, a "read the
    ///     guide", an install link — and daisyUI documents <c>btn</c> on an anchor for exactly it.
    ///     </para>
    ///     <para>
    ///     Given a generated route — <c>Routes.Orders()</c> — it navigates INSIDE the app, as a
    ///     <c>NavLink</c> does: the anchor carries <c>data-rask-nav</c>, which the runtime intercepts and
    ///     routes without reloading the page, and the deploy's path base, so a new tab, a copied link or a
    ///     crawler reaches the same page. A plain string is an ordinary link the browser follows itself, which
    ///     is what a URL that leaves the app wants. With <see cref="NewTab" /> nothing is intercepted: the
    ///     reader asked for another browsing context.
    ///     </para>
    ///     <para>
    ///     It stays ONE component because the tone, fill and size axes are identical either way; a sibling
    ///     would duplicate all of them to change one tag. What does change is what the element means: an
    ///     anchor navigates, so it takes no <c>type</c>, and <see cref="Disabled" /> cannot apply to it —
    ///     there is no disabled state for a link in HTML, and faking one with a class leaves it focusable and
    ///     followable by keyboard. A disabled link is a link that should not be rendered.
    ///     </para>
    ///     <para>
    ///     Sanitised exactly as Core's <c>A</c> sanitises its own, so a <c>javascript:</c> URL that reaches
    ///     a kit button from data is refused the same way.
    ///     </para>
    /// </remarks>
    public RouteUrl? Href { get; set; }

    /// <summary>
    ///     What it does when pressed. Defaults to <see cref="UiButtonType.Button" /> — nothing on its own.
    /// </summary>
    /// <remarks>
    ///     Set <see cref="UiButtonType.Submit" /> for a form's submit button. The default matters in both
    ///     directions and is silent in both: a <c>&lt;button&gt;</c> inside a form submits it unless told
    ///     otherwise, so a toggle that forgot would submit the form around it — and a submit button
    ///     rendered as <c>type="button"</c> does nothing at all when pressed, on a form that looks
    ///     finished. Ignored when <see cref="Href" /> is set; an anchor has no type.
    /// </remarks>
    public UiButtonType? Type { get; set; }

    /// <summary>Opens <see cref="Href" /> in a new tab, with the <c>rel</c> that makes that safe.</summary>
    /// <remarks>
    ///     <c>rel="noopener"</c> comes with it rather than being left to the caller: a new tab opened
    ///     without it can reach back through <c>window.opener</c>, and the one thing a caller will forget
    ///     is the attribute that has no visible effect. Ignored when <see cref="Href" /> is not set.
    /// </remarks>
    public bool? NewTab { get; set; }

    /// <summary>
    ///     Whether it is disabled — by ATTRIBUTE, so the browser refuses the interaction. Only meaningful for
    ///     a button; see the remarks on <see cref="Href" />.
    /// </summary>
    public bool? Disabled { get; set; }

    /// <inheritdoc />
    protected override string TagName => Link is null ? "button" : "a";

    // A null string reaching Href converts to a RouteUrl with no path rather than to no RouteUrl at all, and a
    // button with a null string for a destination is a button, as it was when Href was a string.
    // Href itself rather than `href : null`: that null would take the same string conversion and come out as a
    // RouteUrl with no path, which is the case this exists to catch.
    private RouteUrl? Link => Href is { Path: not null } ? Href : null;

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "btn",
            Tone is { } tone ? UiClassNames.ButtonTone(tone) : "",
            Variant is { } variant ? UiClassNames.ButtonVariant(variant) : "",
            Size is { } size ? UiClassNames.ButtonSize(size) : "",
            Block == true ? "btn-block" : "",
            Wide == true ? "btn-wide" : "",
            Square == true ? "btn-square" : "",
            Circle == true ? "btn-circle" : "",
            Active == true ? "btn-active" : "",
            Class);

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?>? ResolveAria()
    {
        if (AccessibleLabel is not { } label || Aria?.ContainsKey("label") == true)
        {
            return Aria;
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = label };
        if (Aria is { } callerAria)
        {
            foreach (var (name, value) in callerAria)
            {
                aria[name] = value;
            }
        }

        return aria;
    }

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        if (Link is { } href)
        {
            // A generated route carries its page type; a string converted to a RouteUrl does not. Only the
            // first is this app's to route, so only it is intercepted, and only it is prefixed with the
            // deploy's PathBase — a route's own path is prefix-less, exactly as NavLink's is (#975).
            var inApp = href.PageType is not null;
            AppendUrlAttr(sb, "href", inApp ? LiveOptions.PathBase + href.ToString() : href.ToString());

            if (NewTab == true)
            {
                // noopener with it, always — see the remarks on NewTab.
                AppendAttr(sb, "target", "_blank");
                AppendAttr(sb, "rel", "noopener");
            }
            else if (inApp)
            {
                // The runtime's click interception selects on this attribute. Without it the anchor is a full
                // document load, booting the whole app again to reach a page it already has.
                AppendAttr(sb, "data-rask-nav", null);
            }

            // No `type`, no `disabled` and no invoker: none of them means anything on an anchor, and a
            // disabled-looking link is still focusable and still followable.
            return;
        }

        AppendAttr(sb, "type", Type switch
        {
            UiButtonType.Submit => "submit",
            UiButtonType.Reset => "reset",
            _ => "button",
        });

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
}
