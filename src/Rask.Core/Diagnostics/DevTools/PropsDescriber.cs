using System.ComponentModel;
using System.Globalization;

namespace Rask.Core.Diagnostics.DevTools;

/// <summary>
///     What a component tells the devtools about its own properties. Machinery: an app never calls this.
/// </summary>
/// <remarks>
///     <para>
///         Filled by a <c>DescribeProps</c> override the build generates for every component — and only when the
///         devtools are on, so a Release build emits none of it and no app carries a description of its own state.
///     </para>
///     <para>
///         The values are formatted where they are read, by generated code that names each property, so nothing here
///         reflects over a component: reflection would be a trimming hazard in the one place that must survive a trimmed
///         publish intact, and it would read properties whose getters do work.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class PropsDescriber
{
    /// <summary>The longest a described value may be. Past this it is cut, because a panel row is one line.</summary>
    public const int MaxLength = 256;

    /// <summary>What a redacted value reads as.</summary>
    public const string Redacted = "••••";

    private readonly List<DescribedProp> _props = [];

    /// <summary>The properties described so far, in the order they were named.</summary>
    public IReadOnlyList<DescribedProp> Props => _props;

    /// <summary>Describes one property.</summary>
    /// <param name="name">The property's name, as written in the component.</param>
    /// <param name="type">Its declared type, as a developer reads it.</param>
    /// <param name="value">Its value, already formatted.</param>
    public void Add(string name, string type, string? value) =>
        _props.Add(new DescribedProp(name, type, Trim(value), false));

    /// <summary>Describes a property whose value must not be shown — a password, a token, a personal detail.</summary>
    /// <remarks>
    ///     Decided at build time from the property's name and attributes, so the value never reaches a panel, a log or a
    ///     frame; the devtools cannot un-redact what was never sent.
    /// </remarks>
    /// <param name="name">The property's name.</param>
    /// <param name="type">Its declared type.</param>
    public void AddRedacted(string name, string type) => _props.Add(new DescribedProp(name, type, Redacted, true));

    /// <summary>Formats a value the way a panel shows it: invariant, and short enough for one row.</summary>
    /// <remarks>
    ///     Invariant on purpose. A described value is read by the developer against the code they wrote, not by the
    ///     visitor whose culture the page renders in — a decimal shown as "1,5" beside a literal `1.5m` reads as a bug.
    /// </remarks>
    /// <param name="value">The value to format.</param>
    public static string? Format(object? value) => value switch
    {
        null => null,
        string s => Trim(s),
        bool b => b ? "true" : "false",
        IFormattable formattable => Trim(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => Trim(value.ToString()),
    };

    private static string? Trim(string? value) =>
        value is { Length: > MaxLength } ? value[..MaxLength] + "…" : value;
}

/// <summary>One property, as the devtools show it.</summary>
/// <param name="Name">The property's name.</param>
/// <param name="Type">Its declared type.</param>
/// <param name="Value">Its formatted value, or null.</param>
/// <param name="IsRedacted">Whether the value was withheld rather than read.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct DescribedProp(string Name, string Type, string? Value, bool IsRedacted);
