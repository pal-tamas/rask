using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// A box several deploys run against at once, with state: containers and their labels, the network, the proxy's
/// files (those under <c>/data</c> on its volume outlive the proxy container, as on a real box), what Caddy loaded
/// at its last good reload, and the deploy lock. Each deploy talks to it through its own <see cref="Runner"/>, which
/// can hold a step back — that is how a test interleaves two deploys in a chosen order.
/// </summary>
internal sealed class FakeDockerHost
{
    /// <summary>The Caddyfile the stock caddy:2 image starts with.</summary>
    public const string StockCaddyfile = ":80 {\n\troot * /usr/share/caddy\n\tfile_server\n}\n";

    private readonly Lock _gate = new();
    private readonly List<Container> _containers = [];
    private readonly Dictionary<string, string> _proxyFiles = new(StringComparer.Ordinal);
    private IReadOnlyList<string> _loaded = [];
    private int _ids;

    public bool HasNetwork { get; set; } = true;

    /// <summary>Every <c>docker cp</c> into the proxy fails, as a full disk or a dropped connection would.</summary>
    public bool CopyFails { get; set; }

    /// <summary>What Caddy routes now: each site → <c>container:port</c>, from what it loaded at its last good reload.</summary>
    public IReadOnlyDictionary<string, string?> Routes
    {
        get
        {
            lock (_gate)
            {
                return FakeProxy.Sites(_loaded);
            }
        }
    }

    public IReadOnlyList<string> ContainerNames
    {
        get
        {
            lock (_gate)
            {
                return [.. _containers.Select(c => c.Name)];
            }
        }
    }

    public string? ProxyFile(string path)
    {
        lock (_gate)
        {
            return _proxyFiles.GetValueOrDefault(path);
        }
    }

    /// <summary>Start the proxy with <paramref name="caddyfile"/> as its base, and load it.</summary>
    public FakeDockerHost WithProxy(string caddyfile)
    {
        lock (_gate)
        {
            _containers.Add(new Container(DeployCommand.CaddyContainer, NextId(), new Dictionary<string, string>(StringComparer.Ordinal)));
            _proxyFiles[CaddySites.BasePath] = caddyfile;
            Reload();
        }

        return this;
    }

    /// <summary>A file on the proxy's data volume, as an earlier deploy left it.</summary>
    public FakeDockerHost WithProxyFile(string path, string content)
    {
        lock (_gate)
        {
            _proxyFiles[path] = content;
        }

        return this;
    }

    /// <summary>An app colour already running, labelled as a deploy labels it.</summary>
    public FakeDockerHost WithApp(string app, string domain, string color, int port = DeployCommand.DefaultContainerPort)
    {
        lock (_gate)
        {
            _containers.Add(new Container($"{app}-{color}", NextId(), new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["rask.managed"] = "true",
                ["rask.app"] = app,
                ["rask.domain"] = domain,
                ["rask.color"] = color,
                ["rask.port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }));
        }

        return this;
    }

    public Runner For(FakeFileSystem fs) => new(this, fs);

    private ProcessResult Execute(string fileName, IReadOnlyList<string> arguments, FakeFileSystem fs)
    {
        if (!string.Equals(fileName, "docker", StringComparison.Ordinal))
        {
            return DeployCommandTests.IsHostProbe(arguments) ? Ok(DeployCommandTests.ReadyHostProbe) : Ok();
        }

        if (arguments.Count < 2 || !string.Equals(arguments[0], "-H", StringComparison.Ordinal))
        {
            return Ok(); // the local `docker --version` preflight
        }

        var args = arguments.Skip(2).ToArray();
        lock (_gate)
        {
            return args[0] switch
            {
                "ps" => Ok(Listing()),
                "run" => RunContainer(args),
                "rm" => _containers.RemoveAll(c => c.Name == args[^1] || c.Id == args[^1]) > 0 ? Ok() : Fail("No such container"),
                "inspect" => Inspect(args),
                "network" => Network(args),
                "exec" => Exec(args),
                "cp" => Copy(args, fs),
                _ => Ok(), // build, tag, image, stop, logs
            };
        }
    }

    private string Listing() =>
        string.Concat(Enumerable.Reverse(_containers)
            .Where(c => c.Labels.GetValueOrDefault("rask.managed") == "true")
            .Select(c => $"{c.Name}\t{Label(c, "rask.app")}\t{Label(c, "rask.domain")}\t{Label(c, "rask.color")}\t{Label(c, "rask.port")}\t{Label(c, "rask.redirect")}\n"));

    private ProcessResult RunContainer(string[] args)
    {
        string? name = null;
        string? network = null;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        var rest = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d" or "--rm":
                    break;
                case "--name":
                    name = args[++i];
                    break;
                case "--network":
                    network = args[++i];
                    break;
                case "--label":
                    var label = args[++i];
                    labels[label[..label.IndexOf('=', StringComparison.Ordinal)]] = label[(label.IndexOf('=', StringComparison.Ordinal) + 1)..];
                    break;
                case "-e" or "-v" or "-p" or "--log-opt" or "--security-opt" or "--restart" or "--env-file" or "--entrypoint":
                    i++;
                    break;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }

        if (name is null)
        {
            return Probe(network!, rest);
        }

        if (_containers.Any(c => c.Name == name))
        {
            return Fail($"docker: Error response from daemon: Conflict. The container name \"/{name}\" is already in use by container \"x\".", 125);
        }

        var container = new Container(name, NextId(), labels);
        _containers.Add(container);
        if (name == DeployCommand.CaddyContainer)
        {
            // A new proxy container starts from the image's own Caddyfile; its data volume keeps what was on it.
            _proxyFiles[CaddySites.BasePath] = StockCaddyfile;
            Reload();
        }

        return Ok(container.Id + "\n");
    }

    // A throwaway curl container: the readiness probe, the admin API, a request through the proxy, or its upstream.
    private ProcessResult Probe(string network, List<string> command)
    {
        var joined = network["container:".Length..];
        if (!_containers.Any(c => c.Name == joined))
        {
            return Fail("no such container", 125);
        }

        if (command.Contains(ProxyCommands.AdminConfigUrl))
        {
            return Ok(FakeProxy.AdminConfig(_loaded));
        }

        var resolve = command.IndexOf("--resolve");
        if (resolve >= 0)
        {
            var site = command[resolve + 1].Split(':')[0];
            var upstream = FakeProxy.Sites(_loaded).FirstOrDefault(s => s.Key.Split(", ").Contains(site)).Value;
            return Ok(upstream is not null && IsRunning(upstream.Split(':')[0]) ? "200" : "502");
        }

        var url = command[^1];
        return url.StartsWith("http://localhost", StringComparison.Ordinal) || IsRunning(new Uri(url).Host) ? Ok() : Fail("could not resolve host", 6);
    }

    private ProcessResult Inspect(string[] args)
    {
        var container = _containers.FirstOrDefault(c => c.Name == args[^1]);
        if (container is null)
        {
            return Fail($"Error: No such object: {args[^1]}");
        }

        var format = args[2];
        return format.Contains("State.Running", StringComparison.Ordinal) ? Ok("true\n")
            : format.Contains(ProxyCommands.LockOwnerLabel, StringComparison.Ordinal) ? Ok(Label(container, ProxyCommands.LockOwnerLabel) + "\n")
            : Ok(Label(container, "rask.dns") + "\n");
    }

    private ProcessResult Network(string[] args)
    {
        if (args[1] == "inspect")
        {
            return HasNetwork ? Ok("rask\n") : Fail("network rask not found");
        }

        if (HasNetwork)
        {
            return Fail("network with name rask already exists");
        }

        HasNetwork = true;
        return Ok();
    }

    private ProcessResult Exec(string[] args)
    {
        if (!IsRunning(args[1]))
        {
            return Fail($"No such container: {args[1]}");
        }

        string[] command = [.. args.Skip(2)];
        switch (command[0])
        {
            case "cat":
                return _proxyFiles.TryGetValue(command[1], out var content) ? Ok(content) : Fail($"cat: can't open '{command[1]}': No such file or directory");
            case "mkdir" or "touch":
                return Ok();
            case "ls":
                var directory = command[^1] + "/";
                return Ok(string.Concat(_proxyFiles.Keys
                    .Where(path => path.StartsWith(directory, StringComparison.Ordinal) && !path[directory.Length..].Contains('/', StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .Select(path => path[directory.Length..] + "\n")));
            case "mv":
                if (!_proxyFiles.Remove(command[2], out var moved))
                {
                    return Fail("mv: can't rename");
                }

                _proxyFiles[command[3]] = moved;
                return Ok();
            case "rm":
                _proxyFiles.Remove(command[2]);
                return Ok();
            case "caddy":
                return Reload() ? Ok() : Fail("Error: adapting config using caddyfile: ambiguous site definition");
            default:
                return Fail($"unknown command {command[0]}");
        }
    }

    private ProcessResult Copy(string[] args, FakeFileSystem fs)
    {
        if (CopyFails)
        {
            return Fail("Error response from daemon: no space left on device");
        }

        var destination = args[2][(DeployCommand.CaddyContainer.Length + 1)..];
        _proxyFiles[destination] = fs.Written[Path.GetFullPath(args[1])];
        return Ok();
    }

    // What `caddy reload` reads: the base, and — when it imports them — every app's file, in name order, as Caddy's glob does.
    private bool Reload()
    {
        var baseFile = _proxyFiles.GetValueOrDefault(CaddySites.BasePath, string.Empty);
        IReadOnlyList<string> files = CaddySites.IsFragmentLayout(baseFile)
            ? [.. _proxyFiles.Where(f => f.Key.StartsWith(CaddySites.SitesDirectory + "/", StringComparison.Ordinal) && f.Key.EndsWith(CaddySites.FragmentExtension, StringComparison.Ordinal))
                .OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Value)]
            : [baseFile];
        var addresses = files.SelectMany(file => file.Split('\n').Where(line => line.EndsWith(" {", StringComparison.Ordinal) && !line.StartsWith('\t'))).ToList();
        if (addresses.Count != addresses.Distinct(StringComparer.Ordinal).Count())
        {
            return false;
        }

        _loaded = files;
        return true;
    }

    private bool IsRunning(string name) => _containers.Any(c => c.Name == name || c.Id == name);

    private string NextId() => $"id{++_ids:D4}";

    private static string Label(Container container, string key) => container.Labels.GetValueOrDefault(key, "<no value>");

    private static ProcessResult Ok(string output = "") => new(0, output, string.Empty);

    private static ProcessResult Fail(string error, int exitCode = 1) => new(exitCode, string.Empty, error);

    private sealed record Container(string Name, string Id, Dictionary<string, string> Labels);

    /// <summary>One deploy's connection to the box. <see cref="Before"/> runs ahead of each command and may hold it back.</summary>
    internal sealed class Runner(FakeDockerHost host, FakeFileSystem fs) : IProcessRunner
    {
        public Func<IReadOnlyList<string>, Task>? Before { get; set; }

        public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
            (await CaptureAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false)).ExitCode;

        public Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
            RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment);

        public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
        {
            if (Before is not null)
            {
                await Before(arguments).ConfigureAwait(false);
            }

            return host.Execute(fileName, arguments, fs);
        }
    }
}
