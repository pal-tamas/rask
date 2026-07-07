namespace Rask.Bootstrap;

// Typed Bootstrap 5.3 utility classes. Each group is a static class of typed string tokens, joined with
// Bs.Join into a Class string:
//
//   BsCard(Class: Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4)))
//   Div(Class: Bs.Join(Display.Flex(), Flex.Gap(2), Flex.Justify(BsJustify.Between)))
//
// Spacing/display/flex/text-align accept an optional responsive breakpoint (Bp.Md → the -md- infix).
// The text group is named Txt to avoid clashing with the core Text component.

// Responsive breakpoint infix (min-width). Null = applies at all widths (no infix).
public enum Bp
{
    Sm,
    Md,
    Lg,
    Xl,
    Xxl,
}

public enum BsJustify
{
    Start,
    End,
    Center,
    Between,
    Around,
    Evenly,
}

public enum BsAlign
{
    Start,
    End,
    Center,
    Baseline,
    Stretch,
}

// The utility entry point: Bs.Join composes tokens; nested helpers are exposed as the top-level groups
// below.
public static class Bs
{
    // Joins utility tokens (and any raw class strings) into a single Class value, skipping null/empty;
    // returns null when nothing is present so it leaves Class unset rather than emitting class="".
    public static string? Join(params string?[] tokens) => BsClass.Join(tokens);
}

internal static class Breakpoints
{
    // "" for null, else "md-" etc. — the infix Bootstrap inserts between the utility and its value.
    internal static string Infix(this Bp? bp) => bp switch
    {
        Bp.Sm => "sm-",
        Bp.Md => "md-",
        Bp.Lg => "lg-",
        Bp.Xl => "xl-",
        Bp.Xxl => "xxl-",
        _ => "",
    };

    // The bare breakpoint token ("md") for class names that suffix it without a trailing dash,
    // e.g. navbar-expand-md / offcanvas-md.
    internal static string Token(this Bp bp) => bp switch
    {
        Bp.Sm => "sm",
        Bp.Lg => "lg",
        Bp.Xl => "xl",
        Bp.Xxl => "xxl",
        _ => "md",
    };
}

// shadow-* (no responsive variants in Bootstrap).
public static class Shadow
{
    public const string None = "shadow-none";
    public const string Sm = "shadow-sm";
    public const string Default = "shadow";
    public const string Lg = "shadow-lg";
}

// border / border-0 / border-{side} / border-{color}.
public static class Border
{
    public const string All = "border";
    public const string None = "border-0";
    public const string Top = "border-top";
    public const string End = "border-end";
    public const string Bottom = "border-bottom";
    public const string Start = "border-start";
    public const string TopNone = "border-top-0";
    public const string EndNone = "border-end-0";
    public const string BottomNone = "border-bottom-0";
    public const string StartNone = "border-start-0";

    public static string Color(BsColor color) => $"border-{color.Infix()}";
}

// margin: m{side}-{bp?}-{size} (size 0–5). Auto via the *Auto members.
public static class Margin
{
    public static string All(int size, Bp? bp = null) => $"m-{bp.Infix()}{size}";
    public static string Top(int size, Bp? bp = null) => $"mt-{bp.Infix()}{size}";
    public static string Bottom(int size, Bp? bp = null) => $"mb-{bp.Infix()}{size}";
    public static string Start(int size, Bp? bp = null) => $"ms-{bp.Infix()}{size}";
    public static string End(int size, Bp? bp = null) => $"me-{bp.Infix()}{size}";
    public static string X(int size, Bp? bp = null) => $"mx-{bp.Infix()}{size}";
    public static string Y(int size, Bp? bp = null) => $"my-{bp.Infix()}{size}";

    public const string XAuto = "mx-auto";
    public const string StartAuto = "ms-auto";
    public const string EndAuto = "me-auto";
}

// padding: p{side}-{bp?}-{size} (size 0–5).
public static class Padding
{
    public static string All(int size, Bp? bp = null) => $"p-{bp.Infix()}{size}";
    public static string Top(int size, Bp? bp = null) => $"pt-{bp.Infix()}{size}";
    public static string Bottom(int size, Bp? bp = null) => $"pb-{bp.Infix()}{size}";
    public static string Start(int size, Bp? bp = null) => $"ps-{bp.Infix()}{size}";
    public static string End(int size, Bp? bp = null) => $"pe-{bp.Infix()}{size}";
    public static string X(int size, Bp? bp = null) => $"px-{bp.Infix()}{size}";
    public static string Y(int size, Bp? bp = null) => $"py-{bp.Infix()}{size}";
}

// d-{bp?}-{value} display utilities.
public static class Display
{
    public static string None(Bp? bp = null) => $"d-{bp.Infix()}none";
    public static string Inline(Bp? bp = null) => $"d-{bp.Infix()}inline";
    public static string InlineBlock(Bp? bp = null) => $"d-{bp.Infix()}inline-block";
    public static string Block(Bp? bp = null) => $"d-{bp.Infix()}block";
    public static string Flex(Bp? bp = null) => $"d-{bp.Infix()}flex";
    public static string InlineFlex(Bp? bp = null) => $"d-{bp.Infix()}inline-flex";
    public static string Grid(Bp? bp = null) => $"d-{bp.Infix()}grid";
}

// flex utilities (direction / wrap / gap / justify / align / grow-shrink).
public static class Flex
{
    public static string Row(Bp? bp = null) => $"flex-{bp.Infix()}row";
    public static string RowReverse(Bp? bp = null) => $"flex-{bp.Infix()}row-reverse";
    public static string Column(Bp? bp = null) => $"flex-{bp.Infix()}column";
    public static string ColumnReverse(Bp? bp = null) => $"flex-{bp.Infix()}column-reverse";
    public static string Wrap(Bp? bp = null) => $"flex-{bp.Infix()}wrap";
    public static string Nowrap(Bp? bp = null) => $"flex-{bp.Infix()}nowrap";
    public const string Fill = "flex-fill";
    public static string Grow(int n) => $"flex-grow-{n}";
    public static string Shrink(int n) => $"flex-shrink-{n}";

    public static string Gap(int size, Bp? bp = null) => $"gap-{bp.Infix()}{size}";

    public static string Justify(BsJustify value, Bp? bp = null) =>
        $"justify-content-{bp.Infix()}{value switch
        {
            BsJustify.End => "end",
            BsJustify.Center => "center",
            BsJustify.Between => "between",
            BsJustify.Around => "around",
            BsJustify.Evenly => "evenly",
            _ => "start",
        }}";

    public static string Align(BsAlign value, Bp? bp = null) =>
        $"align-items-{bp.Infix()}{value switch
        {
            BsAlign.End => "end",
            BsAlign.Center => "center",
            BsAlign.Baseline => "baseline",
            BsAlign.Stretch => "stretch",
            _ => "start",
        }}";
}

// rounded-* corner utilities.
public static class Rounded
{
    public const string Default = "rounded";
    public const string None = "rounded-0";
    public const string Pill = "rounded-pill";
    public const string Circle = "rounded-circle";
    public const string Top = "rounded-top";
    public const string End = "rounded-end";
    public const string Bottom = "rounded-bottom";
    public const string Start = "rounded-start";

    // rounded-1 … rounded-5 size scale.
    public static string Size(int n) => $"rounded-{n}";
}

// text-* utilities: alignment, color, wrapping, transform. (Named Txt to avoid the core Text
// component. Font weight/style/size live in the Font group, matching Bootstrap's fw-/fst-/fs- prefixes.)
public static class Txt
{
    public static string Start(Bp? bp = null) => $"text-{bp.Infix()}start";
    public static string Center(Bp? bp = null) => $"text-{bp.Infix()}center";
    public static string End(Bp? bp = null) => $"text-{bp.Infix()}end";

    public static string Color(BsColor color) => $"text-{color.Infix()}";

    public const string Muted = "text-body-secondary";
    public const string Truncate = "text-truncate";
    public const string Wrap = "text-wrap";
    public const string Nowrap = "text-nowrap";
    public const string Break = "text-break";
    public const string Uppercase = "text-uppercase";
    public const string Lowercase = "text-lowercase";
    public const string Capitalize = "text-capitalize";
    public const string DecorationNone = "text-decoration-none";
    public const string Underline = "text-decoration-underline";
}

// fw-* / fst-* / fs-* font utilities (weight, style, size).
public static class Font
{
    public const string Bold = "fw-bold";
    public const string Bolder = "fw-bolder";
    public const string Semibold = "fw-semibold";
    public const string Medium = "fw-medium";
    public const string Normal = "fw-normal";
    public const string Light = "fw-light";
    public const string Lighter = "fw-lighter";
    public const string Italic = "fst-italic";
    public const string NotItalic = "fst-normal";

    // .small — ~0.875em, the inline small-print size (Bootstrap's utility form of <small>).
    public const string Small = "small";

    // fs-1 … fs-6 font-size scale.
    public static string Size(int n) => $"fs-{n}";
}

// w-* / h-* / mw-* / vw-* / vh-* sizing utilities (Bootstrap supports 25/50/75/100 + auto).
public static class Sizing
{
    public static string W(int percent) => $"w-{percent}";
    public static string H(int percent) => $"h-{percent}";
    public const string WAuto = "w-auto";
    public const string HAuto = "h-auto";
    public const string MaxW100 = "mw-100";
    public const string MaxH100 = "mh-100";
    public const string VW100 = "vw-100";
    public const string VH100 = "vh-100";
    public const string MinVW100 = "min-vw-100";
    public const string MinVH100 = "min-vh-100";
}

// Bootstrap grid: the .row container and its column spans (.col / .col-auto / .col-{bp?}-{1..12}).
// (Named Grid — not Col/Row — so it doesn't collide with the generated <col>/<tr> element factories.)
// A bare Column(n) also caps width to n/12 of its container, so it centres a card with Margin.XAuto
// without needing a Row parent.
public static class Grid
{
    public const string Row = "row";
    public const string Col = "col";
    public const string ColAuto = "col-auto";
    public static string Column(int n, Bp? bp = null) => $"col-{bp.Infix()}{n}";
    public static string Gutter(int size) => $"g-{size}";
}

// position-* and edge/translate helpers.
public static class Position
{
    public const string Static = "position-static";
    public const string Relative = "position-relative";
    public const string Absolute = "position-absolute";
    public const string Fixed = "position-fixed";
    public const string Sticky = "position-sticky";

    public const string Top0 = "top-0";
    public const string Top50 = "top-50";
    public const string Top100 = "top-100";
    public const string Bottom0 = "bottom-0";
    public const string Start0 = "start-0";
    public const string Start50 = "start-50";
    public const string Start100 = "start-100";
    public const string End0 = "end-0";

    public const string TranslateMiddle = "translate-middle";
    public const string TranslateMiddleX = "translate-middle-x";
    public const string TranslateMiddleY = "translate-middle-y";
}

// bg-* background utilities.
public static class Bg
{
    public static string Color(BsColor color) => $"bg-{color.Infix()}";

    // The contrast-subtle background tint (bg-{color}-subtle) — the light wash used for gentle row/cell
    // emphasis (e.g. warning/negative stock highlighting) rather than the full-strength Color() fill.
    public static string Subtle(BsColor color) => $"bg-{color.Infix()}-subtle";

    public const string Body = "bg-body";
    public const string BodyTertiary = "bg-body-tertiary";
    public const string White = "bg-white";
    public const string Transparent = "bg-transparent";
}
