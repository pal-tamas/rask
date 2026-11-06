using System.Text.RegularExpressions;

namespace Rask.Flux;

/// <summary>The rules every comparison of two computed looks shares.</summary>
public static partial class Compare
{
    /// <summary>
    ///     Colours and lengths print with float noise that differs between two pages computing the same value — and a
    ///     gray's hue, which means nothing at zero chroma, prints as <c>none</c> once a minifier has been at the sheet.
    ///     <paramref name="near" />: two numbers a hair apart can still round to different thousandths (579.8294 and
    ///     579.8295, a translate() Chromium keeps in single precision), so the same text around numbers no further apart
    ///     than that is the same too (parity's rule; the open-list checks round only).
    /// </summary>
    public static bool Same(string? x, string? y, bool near = true)
    {
        if (x == y)
        {
            return true;
        }

        if (x is null || y is null)
        {
            return false;
        }

        if (Rounded(x) == Rounded(y))
        {
            return true;
        }

        if (!near)
        {
            return false;
        }

        var numbers = new List<double>();
        string Shape(string v) => AnyNumber().Replace(Rounded(v), m =>
        {
            numbers.Add(Js.Parse(m.Value));
            return "#";
        });
        if (Shape(x) != Shape(y))
        {
            return false;
        }

        var half = numbers.Count / 2;
        return numbers.Take(half).Select((n, i) => Math.Abs(n - numbers[half + i]) <= 0.0011).All(ok => ok);
    }

    /// <summary>A value with its decimals rounded to thousandths and a hue of <c>none</c> written <c>0</c>.</summary>
    public static string Rounded(string value) =>
        Decimal().Replace(NoneHue().Replace(value, "$1 0)"), m => Js.Num(Js.Round(Js.Parse(m.Value) * 1000) / 1000));

    /// <summary>
    ///     The colour of a border or an outline neither side draws is whatever each PAGE's reset left there — Flux's docs
    ///     default every border to gray-200, a bare preflight to the text colour — not the component's.
    /// </summary>
    public static bool Undrawn(string key, Func<string, string?> x, Func<string, string?> y)
    {
        var side = BorderColor().Match(key);
        if (side.Success)
        {
            var width = $"border{side.Groups[1].Value}Width";
            return x(width) == "0px" && y(width) == "0px";
        }

        return key == "outlineColor" && x("outlineStyle") == "none" && y("outlineStyle") == "none";
    }

    /// <summary><c>Math.round(v * 10) / 10</c>, printed.</summary>
    public static string Fix(double value) => Js.Num(Js.Round(value * 10) / 10);

    [GeneratedRegex(@"(okl(?:ch|ab)\([^)]*?) none\)")]
    private static partial Regex NoneHue();

    [GeneratedRegex("-?[0-9]*\\.[0-9]+(e-?[0-9]+)?")]
    private static partial Regex Decimal();

    [GeneratedRegex("-?[0-9]+(\\.[0-9]+)?(e-?[0-9]+)?")]
    private static partial Regex AnyNumber();

    [GeneratedRegex("^border(Top|Right|Bottom|Left)Color$")]
    private static partial Regex BorderColor();
}
