using System.Globalization;
using System.Text;

namespace Rask;

/// <summary>
/// How a chart writes a number or a date: Flux's <c>:format</c>, which is the options of the browser's
/// <c>Intl.NumberFormat</c> and <c>Intl.DateTimeFormat</c>.
/// </summary>
/// <remarks>
/// <para>
/// The members carry Intl's names and values, and are written with .NET's formatting in the reader's culture:
/// <c>new() { Style = Ui.ChartFormatStyle.Currency, Currency = "USD" }</c>,
/// <c>new() { Notation = Ui.ChartFormatNotation.Compact, MaximumFractionDigits = 1 }</c>,
/// <c>new() { Month = Ui.ChartFormatPart.Short, Day = Ui.ChartFormatPart.Numeric }</c>.
/// </para>
/// <para>
/// A value is a date when its field is one, and takes the date members; anything else takes the number members.
/// Intl options with no member here are not carried: see <c>docs/ui-kit.md</c>.
/// </para>
/// </remarks>
public sealed class UiChartFormat
{
    /// <summary>The kind of number. A plain decimal unless this says otherwise.</summary>
    public Ui.ChartFormatStyle? Style { get; init; }

    /// <summary>With <see cref="Ui.ChartFormatStyle.Currency" />, the ISO 4217 code: <c>"USD"</c>.</summary>
    public string? Currency { get; init; }

    /// <summary>With <see cref="Ui.ChartFormatStyle.Unit" />, Intl's unit name: <c>"megabyte"</c>, <c>"percent"</c>.</summary>
    public string? Unit { get; init; }

    /// <summary>Whether the number is shortened: 1.2K, 1.235E8.</summary>
    public Ui.ChartFormatNotation? Notation { get; init; }

    /// <summary>The fewest digits after the decimal separator.</summary>
    public int? MinimumFractionDigits { get; init; }

    /// <summary>The most digits after the decimal separator.</summary>
    public int? MaximumFractionDigits { get; init; }

    /// <summary>Whether thousands are separated. They are unless this is <c>false</c>.</summary>
    public bool? UseGrouping { get; init; }

    /// <summary>A whole date, at this length.</summary>
    public Ui.ChartFormatLength? DateStyle { get; init; }

    /// <summary>A whole time of day, at this length.</summary>
    public Ui.ChartFormatLength? TimeStyle { get; init; }

    /// <summary>The day of the week: long, short or narrow.</summary>
    public Ui.ChartFormatPart? Weekday { get; init; }

    /// <summary>The year: numeric or two digits.</summary>
    public Ui.ChartFormatPart? Year { get; init; }

    /// <summary>The month, as a number or a name.</summary>
    public Ui.ChartFormatPart? Month { get; init; }

    /// <summary>The day of the month.</summary>
    public Ui.ChartFormatPart? Day { get; init; }

    /// <summary>The hour.</summary>
    public Ui.ChartFormatPart? Hour { get; init; }

    /// <summary>The minute.</summary>
    public Ui.ChartFormatPart? Minute { get; init; }

    /// <summary>The second.</summary>
    public Ui.ChartFormatPart? Second { get; init; }

    /// <summary>A twelve-hour clock with AM and PM, or a twenty-four-hour one. The culture's own unless this says.</summary>
    public bool? Hour12 { get; init; }

    internal string Number(double value)
    {
        var culture = CultureInfo.CurrentCulture;
        return Notation switch
        {
            Ui.ChartFormatNotation.Compact => Affix(Compact(value, culture), culture),
            Ui.ChartFormatNotation.Scientific => value.ToString("0.###E0", culture),
            _ => Style switch
            {
                Ui.ChartFormatStyle.Percent => Digits(value * 100, 0, 0, culture) + culture.NumberFormat.PercentSymbol,
                Ui.ChartFormatStyle.Currency => Affix(Digits(value, 2, 2, culture), culture),
                _ => Affix(Digits(value, 0, 3, culture), culture),
            },
        };
    }

    internal string Date(DateTime value)
    {
        var culture = CultureInfo.CurrentCulture;
        if (DateStyle is not null || TimeStyle is not null)
        {
            var clock = TimeStyle is Ui.ChartFormatLength.Short ? "t" : "T";
            return UiClass.Compose(
                DateStyle is { } date ? value.ToString(DatePattern(date), culture) : null,
                TimeStyle is null ? null : value.ToString(clock, culture));
        }

        return value.ToString(Pattern(culture), culture).Trim();
    }

    private static string DatePattern(Ui.ChartFormatLength length) => length switch
    {
        Ui.ChartFormatLength.Full => "D",
        Ui.ChartFormatLength.Long => "MMMM d, yyyy",
        Ui.ChartFormatLength.Medium => "MMM d, yyyy",
        _ => "d",
    };

    private string Digits(double value, int minimum, int maximum, CultureInfo culture)
    {
        var min = MinimumFractionDigits ?? Math.Min(minimum, MaximumFractionDigits ?? minimum);
        var max = Math.Max(min, MaximumFractionDigits ?? Math.Max(maximum, min));
        var pattern = new StringBuilder(UseGrouping == false ? "0" : "#,##0");
        if (max > 0)
        {
            pattern.Append('.').Append('0', min).Append('#', max - min);
        }

        return value.ToString(pattern.ToString(), culture);
    }

    // Intl's compact notation, short display: the largest of K, M, B, T the number reaches, to two significant
    // digits below a hundred of it unless the fraction digits say otherwise.
    private string Compact(double value, CultureInfo culture)
    {
        ReadOnlySpan<string> suffixes = ["", "K", "M", "B", "T"];
        var magnitude = Math.Abs(value);
        var index = 0;
        while (magnitude >= 1000 && index < suffixes.Length - 1)
        {
            magnitude /= 1000;
            index++;
        }

        var scaled = Math.Sign(value) * magnitude;
        var maximum = MaximumFractionDigits ?? (magnitude < 100 && index > 0 ? 1 : 0);
        var minimum = Math.Min(MinimumFractionDigits ?? 0, maximum);
        return scaled.ToString("0." + new string('0', minimum) + new string('#', maximum - minimum), culture) + suffixes[index];
    }

    private string Affix(string number, CultureInfo culture) => Style switch
    {
        Ui.ChartFormatStyle.Currency => CurrencySymbol(Currency, culture) + number,
        Ui.ChartFormatStyle.Unit => number + UnitSuffix(Unit),
        _ => number,
    };

    private static string CurrencySymbol(string? code, CultureInfo culture) => code?.ToUpperInvariant() switch
    {
        null => culture.NumberFormat.CurrencySymbol,
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        "JPY" => "¥",
        var other => other + " ",
    };

    // Intl's short unit display, for the units a chart is likely to carry; any other is written by name.
    private static string UnitSuffix(string? unit) => unit switch
    {
        null => string.Empty,
        "percent" => "%",
        "byte" => " byte",
        "kilobyte" => " kB",
        "megabyte" => " MB",
        "gigabyte" => " GB",
        "terabyte" => " TB",
        "millisecond" => " ms",
        "second" => " sec",
        "minute" => " min",
        "hour" => " hr",
        "day" => " day",
        "celsius" => "°C",
        "fahrenheit" => "°F",
        "kilometer" => " km",
        "meter" => " m",
        "kilogram" => " kg",
        "gram" => " g",
        "liter" => " L",
        _ => " " + unit,
    };

    // The parts asked for, in the order English writes them; a culture's own separators and names fill them in.
    private string Pattern(CultureInfo culture)
    {
        var numeric = Month is Ui.ChartFormatPart.Numeric or Ui.ChartFormatPart.TwoDigit;
        var date = new StringBuilder();
        Append(date, Weekday switch { null => null, Ui.ChartFormatPart.Long => "dddd", _ => "ddd" }, ", ");
        Append(date, Month switch
        {
            null => null,
            Ui.ChartFormatPart.Numeric => "%M",
            Ui.ChartFormatPart.TwoDigit => "MM",
            Ui.ChartFormatPart.Long => "MMMM",
            _ => "MMM",
        }, Weekday is null ? "" : ", ");
        Append(date, Day switch { null => null, Ui.ChartFormatPart.TwoDigit => "dd", _ => "%d" }, numeric ? "/" : " ");
        var beforeYear = Day is null ? " " : ", ";
        Append(date, Year switch { null => null, Ui.ChartFormatPart.TwoDigit => "yy", _ => "yyyy" }, numeric ? "/" : beforeYear);

        var twelve = Hour12 ?? culture.DateTimeFormat.ShortTimePattern.Contains('h', StringComparison.Ordinal);
        var time = new StringBuilder();
        Append(time, Hour switch
        {
            null => null,
            Ui.ChartFormatPart.TwoDigit => twelve ? "hh" : "HH",
            _ => twelve ? "%h" : "%H",
        }, "");
        Append(time, Minute is null ? null : "mm", ":");
        Append(time, Second is null ? null : "ss", ":");
        if (Hour is not null && twelve)
        {
            time.Append(" tt");
        }

        if (date.Length > 0 && time.Length > 0)
        {
            date.Append(", ");
        }

        var pattern = date.Append(time).ToString().Replace("%", date.Length > 2 ? "" : "%", StringComparison.Ordinal);
        return pattern.Length == 0 ? "d" : pattern;
    }

    private static void Append(StringBuilder pattern, string? part, string separator)
    {
        if (part is null)
        {
            return;
        }

        if (pattern.Length > 0)
        {
            pattern.Append(separator);
        }

        pattern.Append(part);
    }
}
