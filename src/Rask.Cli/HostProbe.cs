using System.ComponentModel;
using System.Globalization;

namespace Rask.Cli;

/// <summary>
/// What a single SSH round-trip found out about a deploy host. Every field is what the box actually
/// reported — the host is the source of truth, so nothing here is remembered between deploys.
/// </summary>
/// <param name="Complete">
/// The probe ran to its <c>end=ok</c> sentinel. When false the output was truncated or garbled and
/// every other field is untrustworthy — critically, "everything is missing" and "we couldn't ask" must
/// never be confused, or we'd cheerfully re-install Docker over a working box.
/// </param>
internal sealed record HostFacts(
    string User,
    bool IsRoot,
    bool HasSystemd,
    bool DockerInstalled,
    bool DockerUsable,
    bool InDockerGroup,
    bool CanSudo,
    bool HasApt,
    bool UfwInstalled,
    bool UfwActive,
    IReadOnlyList<int> SshPorts,
    bool SshConfigInclude,
    bool SshdReadable,
    bool SshRootLoginPermitted,
    bool SshPasswordAuthEnabled,
    bool SshKbdAuthEnabled,
    bool Complete)
{
    /// <summary>The box is ready to deploy to as-is: docker is installed and this user can drive it.</summary>
    public bool DockerReady => DockerInstalled && DockerUsable;

    /// <summary>
    /// Why docker isn't usable, in the user's terms — the distinction
    /// <see cref="DockerProbe.CanReachHostAsync"/> used to collapse into one message.
    /// </summary>
    public string? DockerDiagnosis => (DockerInstalled, DockerUsable, InDockerGroup) switch
    {
        (false, _, _) => "Docker isn't installed",
        (true, false, false) => $"'{User}' isn't in the `docker` group",
        (true, false, true) => "the Docker daemon isn't running",
        _ => null,
    };

    /// <summary>
    /// Parse the probe's <c>key=value</c> lines. Unknown keys are ignored so an older CLI can read a
    /// newer probe; absent keys keep their conservative default (missing/false).
    /// </summary>
    public static HostFacts Parse(string probeOutput)
    {
        var user = "unknown";
        var uid = -1;
        bool systemd = false, docker = false, dockerOk = false, dockerGroup = false;
        bool sudo = false, apt = false, ufw = false, ufwActive = false, sshInclude = false, complete = false;
        bool sshdRead = false, rootLogin = false, passwordAuth = false, kbdAuth = false;
        var sshPorts = new List<int>();

        foreach (var raw in probeOutput.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t');
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq];
            var value = line[(eq + 1)..];
            switch (key)
            {
                case "user": user = value; break;
                case "uid": uid = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : -1; break;
                case "systemd": systemd = Yes(value); break;
                case "docker": docker = Yes(value); break;
                case "dockerok": dockerOk = Yes(value); break;
                case "dockergroup": dockerGroup = Yes(value); break;

                // "root" (uid 0) and "yes" (passwordless sudo) both mean we can run privileged steps.
                case "sudo": sudo = Yes(value) || string.Equals(value, "root", StringComparison.Ordinal); break;
                case "apt": apt = Yes(value); break;
                case "ufw": ufw = Yes(value); break;
                case "ufwactive": ufwActive = string.Equals(value, "active", StringComparison.OrdinalIgnoreCase); break;
                case "sshinclude": sshInclude = Yes(value); break;
                case "sshdread": sshdRead = Yes(value); break;

                // sshd -T prints its keywords and values lower-cased. Anything other than a flat "no"
                // (yes, prohibit-password, forced-commands-only) still lets root in over SSH.
                case "sshrootlogin": rootLogin = !string.Equals(value, "no", StringComparison.Ordinal); break;
                case "sshpasswordauth": passwordAuth = Yes(value); break;
                case "sshkbdauth": kbdAuth = Yes(value); break;
                case "sshport":
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is > 0 and <= 65535 && !sshPorts.Contains(p))
                    {
                        sshPorts.Add(p);
                    }

                    break;
                case "end": complete = string.Equals(value, "ok", StringComparison.Ordinal); break;
                default: break;
            }
        }

        sshPorts.Sort();
        return new HostFacts(
            user, uid == 0, systemd, docker, dockerOk, dockerGroup, sudo, apt, ufw, ufwActive,
            sshPorts, sshInclude, sshdRead, rootLogin, passwordAuth, kbdAuth, complete);

        static bool Yes(string value) => string.Equals(value, "yes", StringComparison.Ordinal);
    }
}

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
            console.Error.WriteLine("`ssh` isn't installed or isn't on your PATH. `rask deploy` needs it to reach the host.");
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
                console.Error.WriteLine();
                console.Error.WriteLine(Indent(detail));
                console.Error.WriteLine();
            }

            console.Error.WriteLine($"Make sure `ssh {target.Destination}` works non-interactively — key-based auth, with the host key already trusted.");
            return null;
        }

        var facts = HostFacts.Parse(result.StandardOutput);
        if (!facts.Complete)
        {
            console.WriteErrorLine($"The host check on '{target}' didn't complete — couldn't tell what's installed on the box.", ConsoleStyle.Error);
            console.Error.WriteLine("This usually means the login shell isn't POSIX-compatible or prints a banner. Rask won't guess and change the host blind.");
            return null;
        }

        return facts;
    }

    private static string Indent(string text) =>
        string.Join('\n', text.Split('\n').Select(line => "  " + line.TrimEnd()));
}
