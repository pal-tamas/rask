using System.Globalization;

namespace Rask.Cli.Tests;

/// <summary>
/// Boots a throwaway container that stands in for a bare VPS: sshd on a published port, its own Docker
/// daemon inside (<c>docker:dind</c>, privileged), and a key-only <c>root</c> login whose identity lives
/// in this fixture's temp directory. <c>rask deploy</c> is then pointed at it exactly as it would be at a
/// real box — <c>docker -H ssh://&lt;host&gt;</c> for every step, no special-casing anywhere in the CLI.
///
/// <para>The host presents as <b>already Docker-ready</b>, so <see cref="HostSetup"/> returns the target
/// untouched (<c>facts.DockerReady &amp;&amp; mode != Forced</c>) and these tests exercise the deploy path
/// rather than the bootstrap path. Bootstrapping a bare box is a separate concern: it installs Docker and
/// rewrites sshd config, which needs a systemd host rather than a dind container.</para>
/// </summary>
public sealed class DeployHostFixture : IAsyncLifetime
{
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private string _root = string.Empty;
    private string _image = string.Empty;
    private bool _started;

    /// <summary>The `--host` value: an ssh-config alias, resolved through this fixture's own config.</summary>
    public string Host => $"rask-e2e-{_id}";

    /// <summary>The fake VPS's container name on the developer's local daemon.</summary>
    public string Container => $"rask-e2e-vps-{_id}";

    /// <summary>Environment for <see cref="EnvScopedProcessRunner"/>: puts the ssh shim first on PATH.</summary>
    public Dictionary<string, string> Environment { get; private set; } = [];

    /// <summary>Executable substitutions for <see cref="EnvScopedProcessRunner"/>: <c>ssh</c> → the shim.</summary>
    public Dictionary<string, string> Executables { get; private set; } = [];

    /// <summary>
    /// Why the fixture couldn't start, when it couldn't. Set only once the gate has been asked for, and
    /// the tests turn it into a <b>failure</b> — being unable to prepare a host the run explicitly
    /// requested is a result, not a reason to report success.
    /// </summary>
    public string? Unavailable { get; private set; }

    public async Task InitializeAsync()
    {
        if (!DeployE2E.Enabled)
        {
            return;
        }

        // The identity is scoped through a POSIX `ssh` shim on PATH (see EnvScopedProcessRunner for why a
        // redirected HOME can't work), so the gate is Unix-only by construction.
        if (OperatingSystem.IsWindows())
        {
            Unavailable = "the deploy host gate needs a Unix host (its ssh shim is a shell script).";
            return;
        }

        var (dockerExit, dockerOut) = await DeployE2E.RunAsync("docker", ["version", "--format", "{{.Server.Version}}"]).ConfigureAwait(false);
        if (dockerExit != 0)
        {
            Unavailable = $"no usable local Docker daemon: {dockerOut.Trim()}";
            return;
        }

        _root = Path.Combine(Path.GetTempPath(), "rask-deploy-e2e", _id);
        Directory.CreateDirectory(_root);

        var key = Path.Combine(_root, "id_e2e");
        var (keyExit, keyOut) = await DeployE2E.RunAsync("ssh-keygen", ["-t", "ed25519", "-N", string.Empty, "-f", key, "-C", "rask-deploy-e2e", "-q"]).ConfigureAwait(false);
        if (keyExit != 0)
        {
            Unavailable = $"ssh-keygen failed: {keyOut.Trim()}";
            return;
        }

        WriteImageContext(key + ".pub");

        _image = $"rask-e2e-vps:{_id}";
        var (buildExit, buildOut) = await DeployE2E.RunAsync("docker", ["build", "-t", _image, _root]).ConfigureAwait(false);
        if (buildExit != 0)
        {
            Unavailable = $"couldn't build the fake-VPS image: {Tail(buildOut)}";
            return;
        }

        var sshPort = DeployE2E.FreePort();
        var (runExit, runOut) = await DeployE2E.RunAsync(
            "docker",
            [
                "run", "-d", "--name", Container, "--privileged",
                // dind serves plain TCP internally; we only ever reach it over ssh, so TLS bootstrap is noise.
                "-e", "DOCKER_TLS_CERTDIR=",
                "-p", $"127.0.0.1:{sshPort.ToString(CultureInfo.InvariantCulture)}:22",
                _image,
            ]).ConfigureAwait(false);
        if (runExit != 0)
        {
            Unavailable = $"couldn't start the fake VPS (privileged containers may be unavailable): {Tail(runOut)}";
            return;
        }

        _started = true;
        WriteSshConfig(key, sshPort);

        // PATH covers the ssh that *docker* spawns for its ssh:// transport; the substitution covers the
        // ssh that Rask spawns directly (see EnvScopedProcessRunner).
        Environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = Path.Combine(_root, "shim") + Path.PathSeparator + System.Environment.GetEnvironmentVariable("PATH"),
        };
        Executables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ssh"] = Path.Combine(_root, "shim", "ssh"),
        };

        if (await WaitForHostAsync().ConfigureAwait(false) is { } reason)
        {
            Unavailable = reason;
        }
    }

    public async Task DisposeAsync()
    {
        if (_started)
        {
            // The container, then the per-run image it was built from — otherwise every run of the gate
            // silently leaves another ~500 MB fake-VPS image behind on the developer's daemon.
            await DeployE2E.RunAsync("docker", ["rm", "-f", Container]).ConfigureAwait(false);
            await DeployE2E.RunAsync("docker", ["image", "rm", "-f", _image]).ConfigureAwait(false);
        }

        if (_root.Length > 0)
        {
            CliBuildE2E.TryDeleteDirectory(_root);
        }
    }

    /// <summary>Run a command on the fake VPS over the same ssh path the CLI uses.</summary>
    public Task<(int Exit, string Output)> OnHostAsync(string command) =>
        DeployE2E.RunAsync(Path.Combine(_root, "shim", "ssh"), ["-o", "BatchMode=yes", "--", Host, command], Environment);

    /// <summary>Run a docker command against the fake VPS's own daemon.</summary>
    public Task<(int Exit, string Output)> DockerAsync(params string[] arguments) =>
        DeployE2E.RunAsync("docker", [.. new[] { "-H", $"ssh://{Host}" }.Concat(arguments)], Environment);

    // The fake VPS: dind (its own dockerd) + sshd, with only our throwaway key trusted.
    private void WriteImageContext(string publicKeyPath)
    {
        File.Copy(publicKeyPath, Path.Combine(_root, "authorized_keys"), overwrite: true);

        // sshd must be running before dockerd takes over PID 1, so it starts first and the dind
        // entrypoint is exec'd (keeping dockerd as the container's main process).
        File.WriteAllText(
            Path.Combine(_root, "entrypoint.sh"),
            """
            #!/bin/sh
            set -e
            [ -f /etc/ssh/ssh_host_ed25519_key ] || ssh-keygen -A
            /usr/sbin/sshd
            exec dockerd-entrypoint.sh "$@"

            """.ReplaceLineEndings("\n"));

        File.WriteAllText(
            Path.Combine(_root, "Dockerfile"),
            $"""
            FROM {DeployE2E.DindImage}
            RUN apk add --no-cache openssh-server
            RUN mkdir -p /root/.ssh && chmod 700 /root/.ssh
            COPY authorized_keys /root/.ssh/authorized_keys
            RUN chmod 600 /root/.ssh/authorized_keys \
             && printf 'PermitRootLogin prohibit-password\nPasswordAuthentication no\n' > /etc/ssh/sshd_config.d/rask-e2e.conf
            COPY entrypoint.sh /usr/local/bin/rask-vps-entrypoint.sh
            RUN chmod +x /usr/local/bin/rask-vps-entrypoint.sh
            ENTRYPOINT ["/usr/local/bin/rask-vps-entrypoint.sh"]

            """.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The ssh config that makes <see cref="Host"/> resolve, plus the shim that makes ssh read it.
    /// <c>UserKnownHostsFile=/dev/null</c> keeps a per-run container host key out of the developer's
    /// known_hosts — the fixture's whole point is to leave no trace outside its temp directory.
    /// </summary>
    private void WriteSshConfig(string keyPath, int sshPort)
    {
        var sshDir = Path.Combine(_root, "ssh");
        Directory.CreateDirectory(sshDir);
        var configPath = Path.Combine(sshDir, "config");
        File.WriteAllText(
            configPath,
            $"""
            Host {Host}
              HostName 127.0.0.1
              Port {sshPort.ToString(CultureInfo.InvariantCulture)}
              User root
              IdentityFile {keyPath}
              IdentitiesOnly yes
              StrictHostKeyChecking no
              UserKnownHostsFile /dev/null
              LogLevel ERROR

            """.ReplaceLineEndings("\n"));

        var shimDir = Path.Combine(_root, "shim");
        Directory.CreateDirectory(shimDir);
        var shim = Path.Combine(shimDir, "ssh");
        File.WriteAllText(
            shim,
            $"""
            #!/bin/sh
            exec /usr/bin/ssh -F "{configPath}" "$@"

            """.ReplaceLineEndings("\n"));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>Wait until sshd answers and dockerd inside the container is up. Returns null once ready.</summary>
    private async Task<string?> WaitForHostAsync()
    {
        string last = "never answered";
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var (exit, output) = await OnHostAsync("docker info >/dev/null 2>&1 && echo rask-vps-ready").ConfigureAwait(false);
            if (exit == 0 && output.Contains("rask-vps-ready", StringComparison.Ordinal))
            {
                return null;
            }

            last = output.Trim();
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        return $"the fake VPS never became ready (sshd + dockerd): {Tail(last)}";
    }

    private static string Tail(string output) =>
        string.Join('\n', output.Split('\n').Where(l => l.Trim().Length > 0).TakeLast(6));
}
