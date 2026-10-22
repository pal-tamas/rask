using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Cli;

/// <summary>
/// The app's stop budget as people write it in <c>.rask/deploy.json</c>: <c>"30m"</c>, <c>"90s"</c>, <c>"1h30m"</c>.
/// </summary>
internal static partial class StopTimeout
{
    /// <summary>The config key, as it appears in <c>.rask/deploy.json</c>.</summary>
    internal const string Key = "stopTimeout";

    /// <summary>Read <paramref name="text"/>; <paramref name="error"/> says how to write one when it isn't.</summary>
    internal static bool TryParse(string text, out TimeSpan value, out string? error) =>
        TryParse(Key, text, ShutdownBudget.MinimumStopTimeout, ShutdownBudget.MaximumStopTimeout, out value, out error);

    /// <summary>Read <paramref name="text"/>, the value of <paramref name="key"/>, as a duration from <paramref name="minimum"/> to <paramref name="maximum"/>.</summary>
    internal static bool TryParse(string key, string text, TimeSpan minimum, TimeSpan maximum, out TimeSpan value, out string? error)
    {
        value = TimeSpan.Zero;
        var match = Duration().Match(text.Trim());
        if (!match.Success || match.Length == 0)
        {
            error = $"\"{key}\": \"{text}\" isn't a duration — write it like \"30m\", \"90s\" or \"1h30m\".";
            return false;
        }

        value = TimeSpan.FromHours(Part(match, "h")) + TimeSpan.FromMinutes(Part(match, "m")) + TimeSpan.FromSeconds(Part(match, "s"));
        if (value < minimum || value > maximum)
        {
            error = $"\"{key}\": \"{text}\" is out of range — between {Format(minimum)} and {Format(maximum)}, written like \"30m\".";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Write <paramref name="value"/> the way <see cref="TryParse"/> reads it: <c>1h30m</c>, <c>30m</c>, <c>1m30s</c>.</summary>
    internal static string Format(TimeSpan value)
    {
        var text = new StringBuilder();
        Append(text, (int)value.TotalHours, 'h');
        Append(text, value.Minutes, 'm');
        Append(text, value.Seconds, 's');
        return text.Length == 0 ? "0s" : text.ToString();
    }

    private static void Append(StringBuilder text, int amount, char unit)
    {
        if (amount > 0)
        {
            text.Append(amount.ToString(CultureInfo.InvariantCulture)).Append(unit);
        }
    }

    private static int Part(Match match, string unit) =>
        match.Groups[unit].Success ? int.Parse(match.Groups[unit].Value, CultureInfo.InvariantCulture) : 0;

    [GeneratedRegex(@"^(?:(?<h>\d{1,3})h)?(?:(?<m>\d{1,5})m)?(?:(?<s>\d{1,6})s)?$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Duration();
}
