using System.Globalization;
using System.Text;

namespace Rask.Api.Client;

/// <summary>
///     Builds the URL of an API call. Called by generated client code; you do not use it directly.
/// </summary>
/// <remarks>
///     Every value goes through <see cref="Uri.EscapeDataString(string)" />, including route segments —
///     an id is not always a number, and a value carrying <c>/</c>, <c>?</c> or <c>#</c> would otherwise
///     change which endpoint the request reaches rather than what it asks for.
/// </remarks>
public static class ApiUri
{
    /// <summary>Renders a value as one path segment.</summary>
    /// <param name="value">The value. Formatted invariantly.</param>
    /// <returns>The escaped segment.</returns>
    /// <exception cref="ArgumentException">
    ///     The value is null or empty. A route token with nothing in it collapses the segment, and the
    ///     request then addresses whatever the shortened path matches — for
    ///     <c>/api/items/{id}</c> that is the <em>collection</em> endpoint, reached with a verb meant for
    ///     one item. Refused here, loudly, rather than sent.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         <see cref="Uri.EscapeDataString(string)" /> alone is NOT enough, which is the whole reason
    ///         this method exists rather than the call site escaping inline. It leaves <c>.</c> and
    ///         <c>..</c> untouched — they are unreserved — and <see cref="HttpClient" /> resolves dot
    ///         segments while combining the path with its base address. So a value of <c>..</c> in
    ///         <c>/api/tenants/{tenant}/orders</c> puts <c>GET /api/orders</c> on the wire: a different
    ///         endpoint, reached with the caller's credentials already attached.
    ///     </para>
    ///     <para>
    ///         Percent-encoding them is the fix rather than rejecting them, because <c>.</c> and
    ///         <c>..</c> are legitimate values for a name or a slug. A server reads <c>%2E%2E</c> as the
    ///         two characters, and does not re-normalise a decoded segment back into a traversal.
    ///     </para>
    /// </remarks>
    public static string Segment(object? value)
    {
        var text = Format(value);

        if (string.IsNullOrEmpty(text))
        {
            throw new ArgumentException(
                "A route value cannot be null or empty: the segment would collapse and the request would "
                + "address a different endpoint.", nameof(value));
        }

        var escaped = Uri.EscapeDataString(text);

        return escaped switch
        {
            "." => "%2E",
            ".." => "%2E%2E",
            _ => escaped,
        };
    }

    /// <summary>
    ///     Renders query parameters, skipping any whose value is null.
    /// </summary>
    /// <param name="parameters">Name/value pairs, in declaration order.</param>
    /// <returns>The query string including its leading <c>?</c>, or empty when nothing was supplied.</returns>
    /// <remarks>
    ///     A null is omitted rather than sent empty, because <c>?page=</c> and no <c>page</c> at all are
    ///     different requests: model binding reads the first as "present and blank" and the second as
    ///     "absent", so an optional parameter left unset must not appear.
    /// </remarks>
    public static string Query(params (string Name, object? Value)[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var builder = new StringBuilder();

        foreach (var (name, value) in parameters)
        {
            switch (value)
            {
                case null:
                    continue;

                // A collection binds as the same name repeated, which is what ASP.NET's binder reads
                // back into an array or a List<T>.
                case System.Collections.IEnumerable sequence and not string:
                    foreach (var item in sequence)
                    {
                        if (item is not null)
                        {
                            Append(builder, name, item);
                        }
                    }

                    continue;

                default:
                    Append(builder, name, value);
                    continue;
            }
        }

        return builder.Length == 0 ? string.Empty : "?" + builder;
    }

    private static void Append(StringBuilder builder, string name, object value)
    {
        if (builder.Length > 0)
        {
            builder.Append('&');
        }

        builder.Append(Uri.EscapeDataString(name))
            .Append('=')
            .Append(Uri.EscapeDataString(Format(value) ?? string.Empty));
    }

    // The formats the generated codecs write on the wire, so a value means the same thing whether it
    // travelled in the body or in the URL. Invariant throughout: a client under a Turkish or German
    // culture must not send "1,5" for 1.5, and that failure is invisible until it runs somewhere else.
    private static string? Format(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset moment => moment.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        Guid id => id.ToString("D", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
