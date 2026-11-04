using System.Text;
using Rask.Core.Components;
using Rask.Core.Diagnostics;

namespace Rask.Core.Forms;

// The values a choice offered in one render: what the browser sends for each part of it the reader can pick —
// every <option> a <select> drew, the value of a radio, the state of a checkbox. A browser only ever sends one of
// those, so a value outside them was never the reader's to send — "999", another tenant's id — and is refused
// (OfferedGuard). The rule is the held select's (UiSelectControl.Held): a value is the form value the render
// wrote, compared ordinally in that spelling, and a disabled part — an option in a disabled group too — offers
// nothing, since the browser never sends one.
//
// A select's options are read where its selection is marked (HTMLSelectElement.EnterChildrenScope): written
// straight under it or under an <optgroup>. A control of one part says what it sends (Choice).
internal sealed class OfferedOptions(string control, string? name)
{
    private readonly List<string> _values = [];

    /// <summary>What one part of a choice offers: what it sends while the reader can use it, nothing while disabled.</summary>
    /// <param name="control">What the control is called in the log: <c>radio</c>, <c>checkbox</c>.</param>
    /// <param name="name">The name it posts under, or null.</param>
    /// <param name="disabled">Whether the part is drawn disabled.</param>
    /// <param name="sends">Each value the browser sends for it.</param>
    internal static OfferedOptions Choice(string control, string? name, bool disabled, params ReadOnlySpan<string> sends)
    {
        var offered = new OfferedOptions(control, name);
        if (!disabled)
        {
            offered._values.AddRange(sends);
        }

        return offered;
    }

    /// <summary>Whether the render drew an option the reader could pick that sends <paramref name="value" />.</summary>
    internal bool Offers(string value) => _values.Contains(value, StringComparer.Ordinal);

    /// <summary>Takes the options of one render, in place of any read before.</summary>
    internal void Read(IEnumerable<Component?> children)
    {
        _values.Clear();
        foreach (var child in children)
        {
            if (child is HTMLOptionElement option)
            {
                Take(option);
            }
            else if (child is HTMLOptGroupElement { Disabled: not true, Children: { } group })
            {
                foreach (var grouped in group)
                {
                    if (grouped is HTMLOptionElement inGroup)
                    {
                        Take(inGroup);
                    }
                }
            }
        }
    }

    /// <summary>Says once, at Warning, that the control refused what it was sent — never what it was sent.</summary>
    internal void Refused() =>
        RaskDiagnostics.Report(
            RaskLogLevel.Warning, "Rask.Forms",
            $"Rask forms: the {control} '{name ?? "(unnamed)"}' was sent a value none of its choices offered; it was refused and the field keeps its value. See https://rask.sh/docs/guides/forms#a-choice-takes-only-what-it-offered");

    private void Take(HTMLOptionElement option)
    {
        if (option.Disabled is not true && SentBy(option) is { } value)
        {
            _values.Add(value);
        }
    }

    // What the browser sends for an option: its value attribute, or without one, its text with the white space
    // stripped and collapsed (HTML's option value). An option drawn from something other than text sends what
    // no render here can know, so it offers nothing.
    private static string? SentBy(HTMLOptionElement option)
    {
        if (option.Value is { } value)
        {
            return value;
        }

        var text = new StringBuilder();
        foreach (var child in option.Children ?? [])
        {
            switch (child)
            {
                case null:
                    break;
                case Text { Value: var said }:
                    text.Append(said);
                    break;
                default:
                    return null;
            }
        }

        return Collapse(text.ToString());
    }

    private static string Collapse(string text) =>
        string.Join(' ', text.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries));
}
