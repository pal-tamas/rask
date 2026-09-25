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
    string DockerFirewall,
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
        var (values, sshPorts) = ReadProbe(probeOutput);

        bool Is(string key, string expected) =>
            values.TryGetValue(key, out var value) && string.Equals(value, expected, StringComparison.Ordinal);
        bool Yes(string key) => Is(key, "yes");

        var uid = values.TryGetValue("uid", out var uidText)
            && int.TryParse(uidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u) ? u : -1;

        return new HostFacts(
            User: values.GetValueOrDefault("user", "unknown"),
            IsRoot: uid == 0,
            HasSystemd: Yes("systemd"),
            DockerInstalled: Yes("docker"),
            DockerUsable: Yes("dockerok"),
            InDockerGroup: Yes("dockergroup"),
            // "root" (uid 0) and "yes" (passwordless sudo) both mean we can run privileged steps.
            CanSudo: Yes("sudo") || Is("sudo", "root"),
            HasApt: Yes("apt"),
            UfwInstalled: Yes("ufw"),
            UfwActive: values.TryGetValue("ufwactive", out var ufwActive)
                && string.Equals(ufwActive, "active", StringComparison.OrdinalIgnoreCase),
            // The signature of the Docker/ufw block already on the box, or empty for "none". It
            // encodes both the rule format and the ports allowed, so changing --port re-plans the
            // step instead of leaving a stale allow-list that would black-hole the new port.
            DockerFirewall: values.GetValueOrDefault("dockerfw", string.Empty),
            SshPorts: sshPorts,
            SshConfigInclude: Yes("sshinclude"),
            SshdReadable: Yes("sshdread"),
            // sshd -T prints its keywords and values lower-cased. Anything other than a flat "no"
            // (yes, prohibit-password, forced-commands-only) still lets root in over SSH.
            SshRootLoginPermitted: values.TryGetValue("sshrootlogin", out var rootLogin)
                && !string.Equals(rootLogin, "no", StringComparison.Ordinal),
            SshPasswordAuthEnabled: Yes("sshpasswordauth"),
            SshKbdAuthEnabled: Yes("sshkbdauth"),
            Complete: Is("end", "ok"));
    }

    /// <summary>
    /// The probe's lines as key/value pairs, the last one winning — except <c>sshport</c>, which sshd
    /// can report several of, so those are collected (valid, distinct and sorted) instead.
    /// </summary>
    private static (Dictionary<string, string> Values, List<int> SshPorts) ReadProbe(string probeOutput)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
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
            if (!string.Equals(key, "sshport", StringComparison.Ordinal))
            {
                values[key] = value;
            }
            else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is > 0 and <= 65535 && !sshPorts.Contains(p))
            {
                sshPorts.Add(p);
            }
        }

        sshPorts.Sort();
        return (values, sshPorts);
    }
}
