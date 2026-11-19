using Rask.Core.Diagnostics;

namespace Rask.Core;

/// <summary>
///     Whether a key of the <c>Data</c>, <c>Aria</c> or <c>Attributes</c> bag can stand as an attribute's name.
/// </summary>
/// <remarks>
///     <para>
///         A value is encoded; a name cannot be — HTML has no escape for one. So a key the app took from a
///         visitor (<c>.Data(userKey, v)</c>) with a space, a quote, <c>&gt;</c> or <c>=</c> in it would end the
///         name and write attributes or markup of the visitor's own: <c>x onmouseover=alert(1)</c>. A key
///         outside HTML's grammar for an attribute's name is not written at all, and said once.
///     </para>
///     <para>
///         The grammar is <see href="https://html.spec.whatwg.org/multipage/syntax.html#attributes-2">HTML's</see>:
///         one or more characters other than controls, space, <c>"</c>, <c>'</c>, <c>&gt;</c>, <c>/</c>,
///         <c>=</c> and noncharacters. <c>&lt;</c> is refused as well: no attribute is named with it, and the
///         browser's <c>setAttribute</c>, which the live diff writes a name through, refuses it too.
///         <c>rask-</c> keys (<c>data-rask-key</c>) are ordinary names and pass.
///     </para>
/// </remarks>
internal static class AttributeName
{
    /// <summary>Whether <paramref name="prefix" /> + <paramref name="key" /> is an attribute's name; reports it once when not.</summary>
    /// <param name="prefix"><c>data-</c>, <c>aria-</c>, or empty for the verbatim bag. Always a name of its own.</param>
    /// <param name="key">The bag's key.</param>
    internal static bool Writes(string prefix, string key)
    {
        if (IsValid(prefix, key))
        {
            return true;
        }

        RaskDiagnostics.ReportOnce(
            "attrname:" + prefix + key,
            RaskLogLevel.Warning,
            "Rask.Render",
            () => $"An attribute named \"{prefix}{key}\" was not written: an attribute's name cannot hold a space, a "
                  + "quote, '<', '>', '/', '=' or a control character, and has no escape for one. A name taken from "
                  + "input is markup of the sender's own; take the name from code and put what varies in the value.");
        return false;
    }

    /// <summary>Whether <paramref name="prefix" /> + <paramref name="key" /> is in HTML's grammar for an attribute's name.</summary>
    internal static bool IsValid(string prefix, string key)
    {
        if (key.Length == 0)
        {
            // "data-" alone is a name; an empty name is not.
            return prefix.Length > 0;
        }

        // A span loop rather than All(): this runs for every bag key on every render, and All allocates.
        foreach (var c in key.AsSpan())
        {
            if (!Allowed(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Allowed(char c) =>
        c > ' '
        && c is not ('"' or '\'' or '<' or '>' or '/' or '=')
        && !char.IsControl(c)
        && c is not ('￾' or '￿')
        && c is not (>= '﷐' and <= '﷯');
}
