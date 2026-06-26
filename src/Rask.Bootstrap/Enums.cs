using System.Globalization;

namespace Rask.Bootstrap;

// Bootstrap's eight theme colors. Used for buttons, alerts, badges, backgrounds, borders, text,
// list-group items, table variants, spinners and progress bars — each component maps the color to
// the relevant Bootstrap class via the helpers in BsClass. (btn-link is intentionally not modelled
// here; it is a link style rather than a theme color — use Class:"btn btn-link" for that.)
public enum BsColor
{
    Primary,
    Secondary,
    Success,
    Danger,
    Warning,
    Info,
    Light,
    Dark,
}

// Sizing scale. Md is the Bootstrap default and emits no size class, so it is the natural
// "unset" value for an optional size parameter.
public enum BsSize
{
    Sm,
    Md,
    Lg,
}

// Bootstrap 5.3 color modes. Emitted as the data-bs-theme attribute. Only the two concrete modes
// are modelled: the docs' "auto" value is resolved to light/dark by a client script, which the
// zero-JS design deliberately does not ship.
public enum BsTheme
{
    Light,
    Dark,
}

// The two Bootstrap spinner styles (.spinner-border / .spinner-grow). Named *Kind to leave the
// BsSpinner name free for the component.
public enum BsSpinnerKind
{
    Border,
    Grow,
}

// Placeholder shimmer animation (.placeholder-glow / .placeholder-wave); None emits no animation.
public enum BsPlaceholderAnimation
{
    None,
    Glow,
    Wave,
}

// Maps the typed enums to their Bootstrap class tokens. A switch expression per mapping keeps the
// translation allocation-free and culture-independent (no enum ToString + ToLower), and keeps every
// emitted class string in one auditable place.
internal static class BsClass
{
    // The lowercase Bootstrap infix for a theme color (Primary -> "primary").
    internal static string Infix(this BsColor color) => color switch
    {
        BsColor.Primary => "primary",
        BsColor.Secondary => "secondary",
        BsColor.Success => "success",
        BsColor.Danger => "danger",
        BsColor.Warning => "warning",
        BsColor.Info => "info",
        BsColor.Light => "light",
        BsColor.Dark => "dark",
        _ => "primary",
    };

    // The size suffix (Sm -> "sm", Lg -> "lg"); Md is the default and has no suffix.
    internal static string? Suffix(this BsSize size) => size switch
    {
        BsSize.Sm => "sm",
        BsSize.Lg => "lg",
        _ => null,
    };

    internal static string Value(this BsTheme theme) => theme == BsTheme.Dark ? "dark" : "light";

    // btn-primary / btn-outline-primary
    internal static string Button(this BsColor color, bool outline) =>
        outline ? $"btn-outline-{color.Infix()}" : $"btn-{color.Infix()}";

    internal static string Alert(this BsColor color) => $"alert-{color.Infix()}";

    // Bootstrap 5.3 badges/contrast helpers use text-bg-* so the text color flips for light backgrounds.
    internal static string TextBg(this BsColor color) => $"text-bg-{color.Infix()}";

    internal static string Bg(this BsColor color) => $"bg-{color.Infix()}";

    internal static string Text(this BsColor color) => $"text-{color.Infix()}";

    internal static string Border(this BsColor color) => $"border-{color.Infix()}";

    internal static string ListGroupItem(this BsColor color) => $"list-group-item-{color.Infix()}";

    internal static string Table(this BsColor color) => $"table-{color.Infix()}";

    // btn-sm / btn-lg
    internal static string? ButtonSize(this BsSize size) => size.Suffix() is { } s ? $"btn-{s}" : null;

    // form-control-sm / form-control-lg (also used, with the prefix swapped, by selects)
    internal static string? ControlSize(this BsSize size, string prefix) =>
        size.Suffix() is { } s ? $"{prefix}-{s}" : null;

    // Invariant numeric text for style widths / aria values (never culture-formatted in markup).
    internal static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    // Returns a copy of an ARIA bag with one extra entry, so a wrapper can add aria-pressed/current
    // on top of a caller-supplied Aria map without mutating the caller's dictionary.
    internal static IReadOnlyDictionary<string, string?> WithAria(
        IReadOnlyDictionary<string, string?>? source, string key, string value)
    {
        var dict = source is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(source);
        dict[key] = value;
        return dict;
    }

    // Joins non-empty class tokens with single spaces, returning null when nothing is present so
    // callers can pass the result straight to a nullable Class parameter without emitting class="".
    internal static string? Join(params string?[] tokens)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(token);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }
}
