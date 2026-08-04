namespace Rask.Cli;

/// <summary>
/// The public host name an app is served on (<c>--domain</c>), validated before it reaches the shared
/// reverse proxy.
///
/// <para><strong>This is a security boundary, not tidiness</strong> — the same one
/// <see cref="SshTarget.TryParse"/> draws, for the same reason. The value is written verbatim into the
/// Caddyfile that fronts <em>every</em> app on the box:</para>
///
/// <code>
/// &lt;domain&gt; {
///     reverse_proxy &lt;container&gt;:&lt;port&gt;
/// }
/// </code>
///
/// <para>A value containing <c>{</c>, <c>}</c> or a newline closes that block and opens another, so a
/// hostile domain injects arbitrary directives into a host-wide proxy config — a global options block, a
/// <c>file_server</c> over <c>/</c>, an open <c>admin</c> endpoint. It also becomes a
/// <c>--label rask.domain=</c> value that is later parsed back out of tab-separated <c>docker ps</c>
/// output, where an embedded newline or tab forges rows.</para>
///
/// <para>And, exactly as with the SSH host, the domain is remembered in <c>.rask/deploy.json</c> — a
/// <em>committed</em> file, read by CI — so a hostile value there would otherwise reconfigure the proxy of
/// every box the repo deploys to.</para>
/// </summary>
internal static class DomainName
{
    /// <summary>Longest legal DNS name, and longest legal label within one (RFC 1035).</summary>
    private const int MaxLength = 253;

    private const int MaxLabelLength = 63;

    /// <summary>
    /// Validate a <c>--domain</c> value. A whitelist, not a blocklist: an RFC-1123 host name, optionally
    /// with a leading <c>*.</c> wildcard label (Caddy accepts one, and it can't widen the character set).
    /// </summary>
    public static bool TryParse(string value, out string domain, out string? error)
    {
        domain = value.Trim();
        error = null;

        if (domain.Length == 0)
        {
            error = "A domain can't be empty.";
            return false;
        }

        if (domain.Length > MaxLength)
        {
            error = $"'{Describe(value)}' isn't a valid domain — it's longer than {MaxLength} characters.";
            return false;
        }

        var labels = domain.Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            // "*.example.com" — a wildcard is only meaningful as the whole first label.
            if (i == 0 && labels[i] == "*" && labels.Length > 1)
            {
                continue;
            }

            if (!IsLabel(labels[i]))
            {
                error = $"'{Describe(value)}' isn't a valid domain — it must be a host name like 'app.example.com' "
                    + "(letters, digits and '-', in dot-separated labels, optionally led by '*.').";
                return false;
            }
        }

        return true;

        static bool IsLabel(string label) =>
            label.Length is > 0 and <= MaxLabelLength
            && !label.StartsWith('-')
            && !label.EndsWith('-')
            && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    /// <summary>
    /// The offending value, made safe to print. The rejected input can itself contain newlines or control
    /// characters, and this message is written to a terminal and to CI logs — echoing it raw would let a
    /// hostile value forge log lines on its way out of the very check that caught it.
    /// </summary>
    private static string Describe(string value)
    {
        var trimmed = value.Trim();
        var sanitized = string.Concat(trimmed.Select(c => char.IsControl(c) ? '?' : c));
        return sanitized.Length > 60 ? sanitized[..60] + "…" : sanitized;
    }
}
