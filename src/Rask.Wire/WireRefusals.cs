using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Rask.Wire;

/// <summary>
///     What a generated codec's rules refused while it read a message a client sent, each failure under the field of the
///     message it came from: a <c>DriverName</c> too long at <c>Driver.Name</c> is a failure about <c>Driver.Name</c>.
/// </summary>
/// <remarks>
///     The codec keeps reading after a refusal, so one answer names every field that is wrong, and the message is
///     never built once anything was refused. Generated code makes one per message read; not an API to write against.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class WireRefusals
{
    private readonly List<Segment> _path = [];
    private List<FieldFailure>? _failures;

    /// <summary>How many failures were recorded so far.</summary>
    public int Count => _failures?.Count ?? 0;

    /// <summary>Every failure recorded, in the order the reader met them; empty when none were.</summary>
    public IReadOnlyList<FieldFailure> Failures => _failures is { } failures ? failures : [];

    /// <summary>Steps into a member of the value being read.</summary>
    /// <param name="member">The member's name, as the contract declares it.</param>
    public void Enter(string member) => _path.Add(new Segment(member, null, 0));

    /// <summary>Steps into one item of a list being read.</summary>
    /// <param name="index">The item's position.</param>
    public void EnterItem(int index) => _path.Add(new Segment(null, null, index));

    /// <summary>Steps into one entry of a map being read.</summary>
    /// <param name="key">The entry's key.</param>
    public void EnterItem(string key) => _path.Add(new Segment(null, key, 0));

    /// <summary>Steps back out of the member, item or entry entered last.</summary>
    public void Leave() => _path.RemoveAt(_path.Count - 1);

    /// <summary>Records that a member that must be given was left out or sent null, under its own field.</summary>
    /// <param name="member">The member's name, as the contract declares it.</param>
    /// <param name="message">What a form's required field says.</param>
    public void Require(string member, string message)
    {
        Enter(member);
        (_failures ??= []).Add(new FieldFailure(message, [Here()]));
        Leave();
    }

    /// <summary>Records what a value object's rules refused, under the field being read.</summary>
    /// <param name="failures">The refusal's failures, each about the whole value or one of its members.</param>
    public void Refuse(IReadOnlyList<FieldFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var here = Here();
        var recorded = _failures ??= [];
        foreach (var failure in failures)
        {
            recorded.Add(failure with { Fields = Under(here, failure.Fields) });
        }
    }

    private static string[] Under(string here, IReadOnlyList<string> fields)
    {
        if (here.Length == 0)
        {
            return [.. fields];
        }

        return fields.Count == 0 ? [here] : [.. fields.Select(field => here + "." + field)];
    }

    // Lines[2].Driver, as a form names the field: a member after a dot, an item in brackets.
    private string Here()
    {
        var path = new StringBuilder();
        foreach (var segment in _path)
        {
            if (segment.Member is { } member)
            {
                if (path.Length > 0)
                {
                    path.Append('.');
                }

                path.Append(member);
            }
            else
            {
                path.Append('[');
                if (segment.Key is { } key)
                {
                    path.Append(key);
                }
                else
                {
                    path.Append(segment.Index.ToString(CultureInfo.InvariantCulture));
                }

                path.Append(']');
            }
        }

        return path.ToString();
    }

    // A member, a map entry's key, or else a list item's index: kept as given, so a read that refuses nothing formats nothing.
    private readonly record struct Segment(string? Member, string? Key, int Index);
}
