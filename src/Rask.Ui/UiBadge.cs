using System.Collections;
using System.Text;

namespace Rask;

/// <summary>
///     Flux's <c>flux:badge</c>: a status, a category or a count, in one of <see cref="Ui.Color" />'s colours.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.Badge.Color(Ui.Color.Lime)["New"]</c>. It IS its element — a <c>&lt;div&gt;</c>, or a
///     <c>&lt;button&gt;</c> with <c>.As(Ui.BadgeAs.Button)</c> — so <c>Id</c>, <c>Class</c>, <c>Data</c> and every event
///     come from <see cref="Element" />: a badge that is pressed is <c>Ui.Badge.As(Ui.BadgeAs.Button).OnClick(Add)["Amount"]</c>.
///     </para>
///     <para>
///     What it says is its children. <see cref="Icon" /> is drawn before them and <see cref="IconTrailing" />
///     after; a <see cref="UiBadgeClose" /> among them makes it removable.
///     </para>
///     <para>
///     It keeps its words on one line, as Flux's does. A long token that has to break instead — a request
///     id in a table cell — takes the utilities at the call site, and the kit's sheet carries them because
///     they are named here: <c>.Class("font-mono max-w-full break-all whitespace-normal!")</c>.
///     </para>
/// </remarks>
public sealed partial class UiBadge : UiElement
{
    private static readonly UiPartMarker Marker = new("ui-badge");

    // Flux's `data-flux-badge-icon`, beside the icon's own marks.
    private static readonly Dictionary<string, string?> LeadingIcon = UiIcon.MarksWith("data-ui-badge-icon");

    /// <summary>Its colour. Unset, zinc.</summary>
    public Ui.Color? Color { get; set; }

    /// <summary>How big it is. Unset, <see cref="Ui.BadgeSize.Base" />.</summary>
    public Ui.BadgeSize? Size { get; set; }

    /// <summary>Rounds the ends fully, and gives them the wider padding a round end needs.</summary>
    public bool? Rounded { get; set; }

    /// <summary>How it is filled. Unset, <see cref="Ui.BadgeVariant.Soft" />.</summary>
    public Ui.BadgeVariant? Variant { get; set; }

    /// <summary>An icon before the words.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>An icon after the words.</summary>
    public Ui.IconName? IconTrailing { get; set; }

    /// <summary>Which drawing of the icons. Unset, <see cref="Ui.IconVariant.Micro" />: 16px, the size of the line.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>The element it is. Unset, <see cref="Ui.BadgeAs.Div" />.</summary>
    public Ui.BadgeAs? As { get; set; }

    /// <summary>The sides whose padding is taken back as a negative margin, for a badge in a line of text.</summary>
    public Ui.Inset? Inset { get; set; }

    /// <inheritdoc />
    protected override string TagName => As == Ui.BadgeAs.Button ? "button" : "div";

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        UiClass.Compose(
            "inline-flex items-center font-medium whitespace-nowrap",
            SizeClass(Size),
            Rounded == true ? "rounded-full px-3" : "rounded-md px-2",
            Variant == Ui.BadgeVariant.Solid ? SolidClass(Color) : SoftClass(Color),
            As == Ui.BadgeAs.Button && Variant != Ui.BadgeVariant.Solid ? SoftHoverClass(Color) : "",
            InsetClass(),
            Class);

    /// <inheritdoc />
    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb)
    {
        base.WriteAttributes(sb);

        // Without it a badge inside a form would submit the form.
        if (As == Ui.BadgeAs.Button)
        {
            AppendAttr(sb, "type", "button");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     An element's children are written straight from what the call site passed, so the icons stand in
    ///     front of and behind them for the length of that walk, and the call site's children are put back
    ///     after it. A badge with no icon takes the walk untouched.
    /// </remarks>
    protected override IDisposable? EnterChildrenScope() =>
        Icon is null && IconTrailing is null ? null : new WithIcons(this);

    private IEnumerable<Component?> Decorate(IEnumerable<Component?>? content)
    {
        var variant = IconVariant ?? Ui.IconVariant.Micro;
        if (Icon is { } icon)
        {
            yield return UiIcon.Marked(LeadingIcon, icon, variant, Size == Ui.BadgeSize.Lg ? "me-2" : "me-1.5");
        }

        foreach (var child in content ?? [])
        {
            yield return child;
        }

        if (IconTrailing is { } trailing)
        {
            yield return Div.Class("flex items-center ps-1").Data("ui-badge-icon:trailing", null)[Ui.Icon.Name(trailing).Variant(variant)];
        }
    }

    private static string SizeClass(Ui.BadgeSize? size) => size switch
    {
        Ui.BadgeSize.Sm => "text-xs py-1",
        Ui.BadgeSize.Lg => "text-sm py-1.5",
        _ => "text-sm py-1",
    };

    // The margin that cancels the padding on that side, so it follows the size and the rounding.
    private string InsetClass()
    {
        if (Inset is not { } inset || inset == Ui.Inset.None)
        {
            return "";
        }

        var top = Size == Ui.BadgeSize.Lg ? "-mt-1.5" : "-mt-1";
        var bottom = Size == Ui.BadgeSize.Lg ? "-mb-1.5" : "-mb-1";
        var left = Rounded == true ? "-ms-3" : "-ms-2";
        var right = Rounded == true ? "-me-3" : "-me-2";
        return UiClass.Compose(
            inset.HasFlag(Ui.Inset.Top) ? top : "",
            inset.HasFlag(Ui.Inset.Bottom) ? bottom : "",
            inset.HasFlag(Ui.Inset.Left) ? left : "",
            inset.HasFlag(Ui.Inset.Right) ? right : "");
    }

    // Measured hue by hue from Flux's page, and it is not one formula: the text is the 700 for the reds and
    // the purples and the 800 from yellow to blue, and amber, yellow and lime are tinted at 25% where the
    // rest are at 20% and zinc at 15%. Dark is the same for all: the 200 on the 400 at 40%.
    private static string SoftClass(Ui.Color? color) => color switch
    {
        Ui.Color.Red => "text-red-700 bg-red-400/20 dark:text-red-200 dark:bg-red-400/40",
        Ui.Color.Orange => "text-orange-700 bg-orange-400/20 dark:text-orange-200 dark:bg-orange-400/40",
        Ui.Color.Amber => "text-amber-700 bg-amber-400/25 dark:text-amber-200 dark:bg-amber-400/40",
        Ui.Color.Yellow => "text-yellow-800 bg-yellow-400/25 dark:text-yellow-200 dark:bg-yellow-400/40",
        Ui.Color.Lime => "text-lime-800 bg-lime-400/25 dark:text-lime-200 dark:bg-lime-400/40",
        Ui.Color.Green => "text-green-800 bg-green-400/20 dark:text-green-200 dark:bg-green-400/40",
        Ui.Color.Emerald => "text-emerald-800 bg-emerald-400/20 dark:text-emerald-200 dark:bg-emerald-400/40",
        Ui.Color.Teal => "text-teal-800 bg-teal-400/20 dark:text-teal-200 dark:bg-teal-400/40",
        Ui.Color.Cyan => "text-cyan-800 bg-cyan-400/20 dark:text-cyan-200 dark:bg-cyan-400/40",
        Ui.Color.Sky => "text-sky-800 bg-sky-400/20 dark:text-sky-200 dark:bg-sky-400/40",
        Ui.Color.Blue => "text-blue-800 bg-blue-400/20 dark:text-blue-200 dark:bg-blue-400/40",
        Ui.Color.Indigo => "text-indigo-700 bg-indigo-400/20 dark:text-indigo-200 dark:bg-indigo-400/40",
        Ui.Color.Violet => "text-violet-700 bg-violet-400/20 dark:text-violet-200 dark:bg-violet-400/40",
        Ui.Color.Purple => "text-purple-700 bg-purple-400/20 dark:text-purple-200 dark:bg-purple-400/40",
        Ui.Color.Fuchsia => "text-fuchsia-700 bg-fuchsia-400/20 dark:text-fuchsia-200 dark:bg-fuchsia-400/40",
        Ui.Color.Pink => "text-pink-700 bg-pink-400/20 dark:text-pink-200 dark:bg-pink-400/40",
        Ui.Color.Rose => "text-rose-700 bg-rose-400/20 dark:text-rose-200 dark:bg-rose-400/40",
        // Flux documents zinc alone among the grays; the other four are drawn as zinc is, on their own scale.
        Ui.Color.Slate => "text-slate-700 bg-slate-400/15 dark:text-slate-200 dark:bg-slate-400/40",
        Ui.Color.Gray => "text-gray-700 bg-gray-400/15 dark:text-gray-200 dark:bg-gray-400/40",
        Ui.Color.Neutral => "text-neutral-700 bg-neutral-400/15 dark:text-neutral-200 dark:bg-neutral-400/40",
        Ui.Color.Stone => "text-stone-700 bg-stone-400/15 dark:text-stone-200 dark:bg-stone-400/40",
        _ => "text-zinc-700 bg-zinc-400/15 dark:text-zinc-200 dark:bg-zinc-400/40",
    };

    // A pressed badge's tint deepens under the pointer. Flux shows it for zinc: ten points in each scheme.
    // The hues follow the same step from their own tint.
    private static string SoftHoverClass(Ui.Color? color) => color switch
    {
        Ui.Color.Red => "hover:bg-red-400/30 dark:hover:bg-red-400/50",
        Ui.Color.Orange => "hover:bg-orange-400/30 dark:hover:bg-orange-400/50",
        Ui.Color.Amber => "hover:bg-amber-400/35 dark:hover:bg-amber-400/50",
        Ui.Color.Yellow => "hover:bg-yellow-400/35 dark:hover:bg-yellow-400/50",
        Ui.Color.Lime => "hover:bg-lime-400/35 dark:hover:bg-lime-400/50",
        Ui.Color.Green => "hover:bg-green-400/30 dark:hover:bg-green-400/50",
        Ui.Color.Emerald => "hover:bg-emerald-400/30 dark:hover:bg-emerald-400/50",
        Ui.Color.Teal => "hover:bg-teal-400/30 dark:hover:bg-teal-400/50",
        Ui.Color.Cyan => "hover:bg-cyan-400/30 dark:hover:bg-cyan-400/50",
        Ui.Color.Sky => "hover:bg-sky-400/30 dark:hover:bg-sky-400/50",
        Ui.Color.Blue => "hover:bg-blue-400/30 dark:hover:bg-blue-400/50",
        Ui.Color.Indigo => "hover:bg-indigo-400/30 dark:hover:bg-indigo-400/50",
        Ui.Color.Violet => "hover:bg-violet-400/30 dark:hover:bg-violet-400/50",
        Ui.Color.Purple => "hover:bg-purple-400/30 dark:hover:bg-purple-400/50",
        Ui.Color.Fuchsia => "hover:bg-fuchsia-400/30 dark:hover:bg-fuchsia-400/50",
        Ui.Color.Pink => "hover:bg-pink-400/30 dark:hover:bg-pink-400/50",
        Ui.Color.Rose => "hover:bg-rose-400/30 dark:hover:bg-rose-400/50",
        Ui.Color.Slate => "hover:bg-slate-400/25 dark:hover:bg-slate-400/50",
        Ui.Color.Gray => "hover:bg-gray-400/25 dark:hover:bg-gray-400/50",
        Ui.Color.Neutral => "hover:bg-neutral-400/25 dark:hover:bg-neutral-400/50",
        Ui.Color.Stone => "hover:bg-stone-400/25 dark:hover:bg-stone-400/50",
        _ => "hover:bg-zinc-400/25 dark:hover:bg-zinc-400/50",
    };

    // White on the 500, and on the 600 in dark — except amber and yellow, which are too light to carry
    // white there: dark keeps amber's 500 and takes yellow's 400, under near-black text. Zinc is its 600 in both.
    private static string SolidClass(Ui.Color? color) => color switch
    {
        Ui.Color.Red => "text-white bg-red-500 dark:bg-red-600",
        Ui.Color.Orange => "text-white bg-orange-500 dark:bg-orange-600",
        Ui.Color.Amber => "text-white bg-amber-500 dark:text-zinc-950",
        Ui.Color.Yellow => "text-white bg-yellow-500 dark:text-zinc-950 dark:bg-yellow-400",
        Ui.Color.Lime => "text-white bg-lime-500 dark:bg-lime-600",
        Ui.Color.Green => "text-white bg-green-500 dark:bg-green-600",
        Ui.Color.Emerald => "text-white bg-emerald-500 dark:bg-emerald-600",
        Ui.Color.Teal => "text-white bg-teal-500 dark:bg-teal-600",
        Ui.Color.Cyan => "text-white bg-cyan-500 dark:bg-cyan-600",
        Ui.Color.Sky => "text-white bg-sky-500 dark:bg-sky-600",
        Ui.Color.Blue => "text-white bg-blue-500 dark:bg-blue-600",
        Ui.Color.Indigo => "text-white bg-indigo-500 dark:bg-indigo-600",
        Ui.Color.Violet => "text-white bg-violet-500 dark:bg-violet-600",
        Ui.Color.Purple => "text-white bg-purple-500 dark:bg-purple-600",
        Ui.Color.Fuchsia => "text-white bg-fuchsia-500 dark:bg-fuchsia-600",
        Ui.Color.Pink => "text-white bg-pink-500 dark:bg-pink-600",
        Ui.Color.Rose => "text-white bg-rose-500 dark:bg-rose-600",
        Ui.Color.Slate => "text-white bg-slate-600",
        Ui.Color.Gray => "text-white bg-gray-600",
        Ui.Color.Neutral => "text-white bg-neutral-600",
        Ui.Color.Stone => "text-white bg-stone-600",
        _ => "text-white bg-zinc-600",
    };

    // The badge's children for one walk: its icons around what the call site passed. Enumerated by the
    // serializer, which is where the icons are built — the same place a children-function builds its own.
    private sealed class WithIcons : IEnumerable<Component?>, IDisposable
    {
        private readonly UiBadge _badge;
        private readonly IEnumerable<Component?>? _content;

        public WithIcons(UiBadge badge)
        {
            _badge = badge;
            _content = badge.Children;
            badge.Children = this;
        }

        public IEnumerator<Component?> GetEnumerator() => _badge.Decorate(_content).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose() => _badge.Children = _content;
    }
}
