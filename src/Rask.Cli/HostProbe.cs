using System.ComponentModel;
using System.Globalization;

namespace Rask.Cli;

/// <summary>
/// Asks a deploy host what it is, in one SSH round-trip, before <c>rask deploy</c> touches it.
///
/// <para>This replaces the old <c>docker -H ssh://&lt;host&gt; version</c> reachability check rather
/// than adding to it, so preflight still costs exactly one round-trip — but instead of a single
/// boolean it comes back with enough detail to either fix the box (<see cref="HostBootstrap"/>) or
/// tell the user precisely what's wrong.</para>
/// </summary>
internal static class HostProbe
{
    /// <summary>
    /// A POSIX-sh probe: read-only, no side effects, safe to run on any box. Every fact is emitted as a
    /// <c>key=value</c> line and the script closes with <c>end=ok</c> so a half-delivered result is
    /// detectable. <c>sshd -T</c> is tried through sudo and by absolute path because it lives in
    /// <c>/usr/sbin</c>, which isn't on a non-root PATH.
    /// </summary>
    internal const string ProbeScript = """
        printf 'user=%s\n' "$(id -un 2>/dev/null || echo unknown)"
        printf 'uid=%s\n' "$(id -u 2>/dev/null || echo -1)"
        if command -v systemctl >/dev/null 2>&1; then printf 'systemd=yes\n'; else printf 'systemd=no\n'; fi
        if command -v docker >/dev/null 2>&1; then printf 'docker=yes\n'; else printf 'docker=no\n'; fi
        if docker info >/dev/null 2>&1; then printf 'dockerok=yes\n'; else printf 'dockerok=no\n'; fi
        if id -nG 2>/dev/null | tr ' ' '\n' | grep -qx docker; then printf 'dockergroup=yes\n'; else printf 'dockergroup=no\n'; fi
        if [ "$(id -u)" = 0 ]; then printf 'sudo=root\n'; elif sudo -n true >/dev/null 2>&1; then printf 'sudo=yes\n'; else printf 'sudo=no\n'; fi
        if command -v apt-get >/dev/null 2>&1; then printf 'apt=yes\n'; else printf 'apt=no\n'; fi
        if command -v ufw >/dev/null 2>&1; then printf 'ufw=yes\n'; else printf 'ufw=no\n'; fi
        printf 'ufwactive=%s\n' "$( { sudo -n ufw status 2>/dev/null || ufw status 2>/dev/null; } | sed -n 's/^Status: //p' | head -1)"
        printf 'dockerfw=%s\n' "$( { sudo -n sed -n 's/^###RASK-DOCKER-BEGIN \([^ ]*\).*/\1/p' /etc/ufw/after.rules 2>/dev/null || sed -n 's/^###RASK-DOCKER-BEGIN \([^ ]*\).*/\1/p' /etc/ufw/after.rules 2>/dev/null; } | head -1)"
        if grep -qE '^[[:space:]]*Include[[:space:]]+/etc/ssh/sshd_config\.d/\*\.conf' /etc/ssh/sshd_config 2>/dev/null; then printf 'sshinclude=yes\n'; else printf 'sshinclude=no\n'; fi
        SSHD_T="$( { sudo -n sshd -T 2>/dev/null || sshd -T 2>/dev/null || sudo -n /usr/sbin/sshd -T 2>/dev/null || /usr/sbin/sshd -T 2>/dev/null; } )"
        if [ -n "$SSHD_T" ]; then
          printf 'sshdread=yes\n'
          printf '%s\n' "$SSHD_T" | awk '/^port /{printf "sshport=%s\n",$2} /^permitrootlogin /{printf "sshrootlogin=%s\n",$2} /^passwordauthentication /{printf "sshpasswordauth=%s\n",$2} /^kbdinteractiveauthentication /{printf "sshkbdauth=%s\n",$2}'
        else
          printf 'sshdread=no\n'
        fi
        printf 'end=ok\n'
        """;

    internal static IReadOnlyList<string> BuildArguments(SshTarget target) =>
        [.. target.ConnectionArguments(), ProbeScript];

    /// <summary>
    /// Probe <paramref name="target"/>. Returns the facts, or <c>null</c> after printing why we
    /// couldn't ask — an unreachable box is a different problem from an unprepared one, and the user
    /// gets ssh's own words rather than a guess.
    /// </summary>
    public static async Task<HostFacts?> ProbeAsync(IProcessRunner process, IConsole console, SshTarget target, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await process.CaptureAsync("ssh", BuildArguments(target), null, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            // No ssh binary at all — launching it throws rather than returning non-zero.
            await console.Error.WriteLineAsync("`ssh` isn't installed or isn't on your PATH. `rask deploy` needs it to reach the host.").ConfigureAwait(false);
            return null;
        }

        if (result.ExitCode != 0)
        {
            console.WriteErrorLine($"Couldn't connect to '{target}' over SSH.", ConsoleStyle.Error);
            var detail = result.StandardError.Trim();
            if (detail.Length > 0)
            {
                // ssh already explained itself (permission denied / host key / name resolution) —
                // its message beats anything we'd invent.
                await console.Error.WriteLineAsync().ConfigureAwait(false);
                await console.Error.WriteLineAsync(Indent(detail)).ConfigureAwait(false);
                await console.Error.WriteLineAsync().ConfigureAwait(false);
            }

            await console.Error.WriteLineAsync($"Make sure `ssh {target.Destination}` works non-interactively — key-based auth, with the host key already trusted.").ConfigureAwait(false);
            return null;
        }

        var facts = HostFacts.Parse(result.StandardOutput);
        if (!facts.Complete)
        {
            console.WriteErrorLine($"The host check on '{target}' didn't complete — couldn't tell what's installed on the box.", ConsoleStyle.Error);
            await console.Error.WriteLineAsync("This usually means the login shell isn't POSIX-compatible or prints a banner. Rask won't guess and change the host blind.").ConfigureAwait(false);
            return null;
        }

        return facts;
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(line => "  " + line.TrimEnd()));
}
