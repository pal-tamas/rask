namespace Rask.Cli.Tests;

/// <summary>
/// <see cref="HostFacts.Parse"/> turns the probe's <c>key=value</c> lines into the facts that decide
/// what <c>rask deploy</c> does to a host. Everything here is pure — no ssh, no box.
/// </summary>
public class HostFactsParseTests
{
    /// <summary>A fully-provisioned host: docker installed and usable, ufw up, non-root deploy user.</summary>
    private const string ReadyHost = """
        user=deploy
        uid=1000
        systemd=yes
        docker=yes
        dockerok=yes
        dockergroup=yes
        sudo=yes
        ufw=yes
        ufwactive=active
        sshport=22
        end=ok
        """;

    /// <summary>A fresh VPS: root over SSH and nothing else.</summary>
    private const string BareHost = """
        user=root
        uid=0
        systemd=yes
        docker=no
        dockerok=no
        dockergroup=no
        sudo=root
        ufw=no
        ufwactive=
        sshport=22
        end=ok
        """;

    [Fact]
    public void Reads_a_ready_host()
    {
        var facts = HostFacts.Parse(ReadyHost);

        Assert.True(facts.Complete);
        Assert.Equal("deploy", facts.User);
        Assert.False(facts.IsRoot);
        Assert.True(facts.HasSystemd);
        Assert.True(facts.DockerReady);
        Assert.True(facts.CanSudo);
        Assert.True(facts.UfwActive);
        Assert.Equal([22], facts.SshPorts);
        Assert.Null(facts.DockerDiagnosis);
    }

    [Fact]
    public void Reads_a_bare_host()
    {
        var facts = HostFacts.Parse(BareHost);

        Assert.True(facts.Complete);
        Assert.True(facts.IsRoot);
        Assert.True(facts.CanSudo); // uid 0 needs no sudo
        Assert.False(facts.DockerInstalled);
        Assert.False(facts.DockerReady);
        Assert.False(facts.UfwInstalled);
        Assert.False(facts.UfwActive);
    }

    [Fact]
    public void An_incomplete_probe_is_never_mistaken_for_an_empty_host()
    {
        // The whole point of the end=ok sentinel: a truncated probe must not read as "nothing is
        // installed", or we'd re-install Docker over a working box.
        var facts = HostFacts.Parse("user=root\nuid=0\ndocker=yes\n");

        Assert.False(facts.Complete);
    }

    [Fact]
    public void An_empty_probe_is_incomplete()
    {
        Assert.False(HostFacts.Parse(string.Empty).Complete);
        Assert.False(HostFacts.Parse("   \n \n").Complete);
    }

    [Theory]
    [InlineData("sudo=root", true)]  // uid 0
    [InlineData("sudo=yes", true)]   // passwordless sudo
    [InlineData("sudo=no", false)]
    public void Root_and_passwordless_sudo_both_count_as_privileged(string line, bool expected) =>
        Assert.Equal(expected, HostFacts.Parse($"{line}\nend=ok").CanSudo);

    [Fact]
    public void Collects_every_ssh_port_sorted_and_deduped()
    {
        // sshd can listen on several ports; the firewall must allow all of them or we lock ourselves out.
        var facts = HostFacts.Parse("sshport=2222\nsshport=22\nsshport=2222\nend=ok");

        Assert.Equal([22, 2222], facts.SshPorts);
    }

    [Theory]
    [InlineData("sshport=notaport")]
    [InlineData("sshport=0")]
    [InlineData("sshport=70000")]
    [InlineData("sshport=")]
    public void Rejects_an_unusable_ssh_port_rather_than_guessing(string line)
    {
        // An empty port list is what makes the firewall refuse to enable — far better than assuming 22.
        Assert.Empty(HostFacts.Parse($"{line}\nend=ok").SshPorts);
    }

    [Fact]
    public void Ufw_is_active_only_when_the_host_says_active()
    {
        Assert.True(HostFacts.Parse("ufwactive=active\nend=ok").UfwActive);
        Assert.False(HostFacts.Parse("ufwactive=inactive\nend=ok").UfwActive);
        Assert.False(HostFacts.Parse("ufwactive=\nend=ok").UfwActive);
    }

    [Fact]
    public void The_docker_firewall_signature_is_read_back_verbatim()
    {
        // It decides whether the Docker/ufw block on the box is the one we'd write. Absent means
        // "no block", which is also what an older Rask's probe output looks like to a newer CLI.
        Assert.Equal("v1:80,443", HostFacts.Parse("dockerfw=v1:80,443\nend=ok").DockerFirewall);
        Assert.Equal(string.Empty, HostFacts.Parse("dockerfw=\nend=ok").DockerFirewall);
        Assert.Equal(string.Empty, HostFacts.Parse("end=ok").DockerFirewall);
    }

    [Fact]
    public void The_probe_reads_the_docker_firewall_block_without_writing_to_it()
    {
        // `sed -n …p` prints; `sed -i` would edit the live firewall config during a read-only probe,
        // before the user has agreed to anything.
        Assert.Contains("dockerfw=", HostProbe.ProbeScript, StringComparison.Ordinal);
        Assert.DoesNotContain("sed -i", HostProbe.ProbeScript, StringComparison.Ordinal);
    }

    [Fact]
    public void Tolerates_crlf_and_stray_whitespace()
    {
        // A box whose shell prints \r\n shouldn't read as a box with nothing installed.
        var facts = HostFacts.Parse("user=deploy\r\ndocker=yes\r\n  dockerok=yes  \r\nend=ok\r\n");

        Assert.True(facts.Complete);
        Assert.True(facts.DockerReady);
        Assert.Equal("deploy", facts.User);
    }

    [Fact]
    public void Ignores_unknown_keys_and_banner_noise()
    {
        // Login banners and a newer probe's extra keys must not derail the parse.
        var facts = HostFacts.Parse("Welcome to Ubuntu!\nfuturekey=whatever\ndocker=yes\ndockerok=yes\nend=ok");

        Assert.True(facts.Complete);
        Assert.True(facts.DockerReady);
    }

    [Theory]
    // (installed, usable, in group) → the plain-language reason the old probe couldn't distinguish.
    [InlineData("docker=no\ndockerok=no\ndockergroup=no", "Docker isn't installed")]
    [InlineData("docker=yes\ndockerok=no\ndockergroup=no", "'deploy' isn't in the `docker` group")]
    [InlineData("docker=yes\ndockerok=no\ndockergroup=yes", "the Docker daemon isn't running")]
    public void Diagnoses_each_docker_failure_separately(string lines, string expected) =>
        Assert.Equal(expected, HostFacts.Parse($"user=deploy\n{lines}\nend=ok").DockerDiagnosis);

    [Fact]
    public void A_ready_host_has_no_diagnosis() =>
        Assert.Null(HostFacts.Parse("docker=yes\ndockerok=yes\nend=ok").DockerDiagnosis);
}

/// <summary>The ssh invocation carrying the probe script.</summary>
public class HostProbeTests
{
    [Fact]
    public void Builds_a_single_ssh_round_trip_carrying_the_probe_script()
    {
        var args = HostProbe.BuildArguments(SshTarget.Parse("root@box"));

        Assert.Equal(["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "--", "root@box", HostProbe.ProbeScript], args);
    }

    [Fact]
    public void The_probe_script_is_read_only_and_ends_with_the_sentinel()
    {
        // The probe runs BEFORE the user has consented to anything, so it must not change the box.
        // Only actual mutations are banned — `command -v apt-get` is a question, `apt-get install` is not.
        Assert.EndsWith("printf 'end=ok\\n'", HostProbe.ProbeScript, StringComparison.Ordinal);
        foreach (var mutation in new[]
        {
            "apt-get install", "apt-get update", "dnf install", "yum install", "curl ", "useradd", "usermod",
            "groupadd", "ufw allow", "ufw --force", "systemctl enable", "systemctl reload", "systemd-run", "rm ", "mv ", "install -",
        })
        {
            Assert.DoesNotContain(mutation, HostProbe.ProbeScript, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_unreachable_host_reports_sshs_own_words_rather_than_a_guess()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner
        {
            CaptureResult = new ProcessResult(255, string.Empty, "ssh: connect to host box port 22: Connection refused"),
        };

        var facts = await HostProbe.ProbeAsync(runner, console, SshTarget.Parse("root@box"), CancellationToken.None);

        Assert.Null(facts);
        Assert.Contains("Connection refused", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Couldn't connect to 'root@box'", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_incomplete_probe_refuses_to_guess_at_the_host()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "user=root\ndocker=no\n", string.Empty) };

        var facts = await HostProbe.ProbeAsync(runner, console, SshTarget.Parse("root@box"), CancellationToken.None);

        Assert.Null(facts);
        Assert.Contains("didn't complete", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_complete_probe_returns_the_facts()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner
        {
            CaptureResult = new ProcessResult(0, "user=root\nuid=0\ndocker=yes\ndockerok=yes\nend=ok", string.Empty),
        };

        var facts = await HostProbe.ProbeAsync(runner, console, SshTarget.Parse("root@box"), CancellationToken.None);

        Assert.NotNull(facts);
        Assert.True(facts.DockerReady);
        Assert.Equal(string.Empty, console.ErrorText);
    }
}
