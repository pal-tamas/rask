using System.Text;
using Rask.Core.Components;
using Rask.Core.Diagnostics;

namespace Rask.Core.Forms;

// The values a <select> offered in one render: every <option> it drew that the reader can pick. A browser
// only ever sends one of those, so a value outside them was never the reader's to send — "999", another
// tenant's id — and is refused (OfferedGuard). The rule is the held select's (UiSelectControl.Held): a value
// is the form value an option wrote, compared ordinally in the spelling the render wrote, and a disabled
// option — or one in a disabled group — offers nothing, since the browser never sends one.
//
// The options are read where the selection is marked (HTMLSelectElement.EnterChildrenScope): written straight
// under the select or under an <optgroup>.
internal sealed class OfferedOptions
{
    private readonly List<string> _values = [];

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

    /// <summary>Says once, at Warning, that a select refused what it was sent — never what it was sent.</summary>
    /// <param name="name">The name the select posts under, or null.</param>
    internal static void Refused(string? name) =>
        RaskDiagnostics.Report(
            RaskLogLevel.Warning, "Rask.Forms",
            $"Rask forms: the select '{name ?? "(unnamed)"}' was sent a value none of its options offered; it was refused and the field keeps its value. See https://rask.sh/docs/guides/forms#a-select-takes-only-what-it-offered");

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
