using Rask.Cli.Scaffolding;
using static Rask.Cli.Commands.ProxyCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask host</c> — look after the box <c>rask deploy</c> ships to, rather than an app on it. <c>clean</c> gives the
/// box its disk back on demand: the same two prunes every deploy that serves runs (<see cref="HostCleanUp"/>), under
/// the same box lock, so it never runs while a deploy is switching the shared proxy — it waits for that deploy the way
/// a deploy waits for another, and gives up after one lease.
/// </summary>
internal sealed class HostCommand : CliCommand
{
    /// <summary>Who holds the box's lock while this runs — what a deploy waiting for it is told.</summary>
    internal const string LockOwner = "rask host clean";

    private readonly IFileSystem _fileSystem;
    private readonly IProcessRunner _process;
    private readonly string _workingDirectory;

    public HostCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
        : base(console)
    {
        _fileSystem = fileSystem;
        // Each prune is held to the deadline a deploy's is (DeployDeadlines.Prune), so both end inside the lock's lease.
        _process = new DeadlineRunner(process);
        _workingDirectory = workingDirectory;
    }

    /// <summary>How long to wait between attempts at the box's lock while a deploy holds it — zero in tests.</summary>
    internal TimeSpan LockPollDelay { get; set; } = TimeSpan.FromSeconds(LockPollSeconds);

    /// <summary>How many attempts at the box's lock before giving up — past a full lease, as a deploy's.</summary>
    internal int LockAttempts { get; set; } = (LockLeaseSeconds / LockPollSeconds) + 20;

    public override string Name => "host";

    public override string Summary => "Look after the box rask deploy ships to: free the disk old deploys left behind.";

    public override string Usage => "rask host <action> [options]";

    public override IReadOnlyList<string> Examples =>
    [
        "rask host clean",
        "rask host clean --host deploy@box.example.com",
        "rask host clean --dry-run",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Verb("clean", "Remove untagged images and trim the build cache, under the box's deploy lock, and say what was freed.")
            .Option("host", null, "user@host", "The box to clean (default: the one in .rask/deploy.json).")
            .Flag("dry-run", description: "Print the docker commands that would run without changing anything.");

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();
        var verb = args.Count > 0 ? args[0] : null;
        if (!schema.TryResolveVerb(verb, out _))
        {
            return FailUnknownVerb(verb, schema);
        }

        var parsed = schema.Parse(args.Skip(1).ToArray());
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 0)
        {
            return Fail($"Unexpected argument '{parsed.Positionals[0]}'.");
        }

        var config = DeployConfig.Load(_fileSystem, _workingDirectory, Console);
        var host = parsed.Option("host") ?? config.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return Fail("No host to clean. Pass --host user@host, or run it where .rask/deploy.json remembers one.");
        }

        host = host.Trim();
        // The host reaches ssh as an argument and may come from the committed deploy.json: validated, as a deploy does.
        if (!SshTarget.TryParse(host, out var target, out var hostError))
        {
            return Fail(hostError!);
        }

        if (!DiskPolicy.TryFrom(config, out var policy, out var policyError))
        {
            return Fail($"{Path.Combine(".rask", "deploy.json")}: {policyError}");
        }

        return parsed.HasFlag("dry-run")
            ? PrintPlan(host, policy)
            : await CleanAsync(host, target.Host, policy, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> CleanAsync(string host, string box, DiskPolicy policy, CancellationToken cancellationToken)
    {
        WriteHeading($"Cleaning up old images and build cache on {box}…");
        var cleanUp = new HostCleanUp(Console, _process, _workingDirectory);
        var (outcome, failure) = await cleanUp
            .RunAsync(host, LockOwner, policy.BuildCacheKeepArgument, new LockTiming(LockPollDelay, LockAttempts, TimeSpan.Zero), ConsoleStyle.Error, cancellationToken)
            .ConfigureAwait(false);
        switch (outcome)
        {
            case HostCleanUp.Outcome.Cleaned:
                return 0;
            case HostCleanUp.Outcome.Failed:
                Console.WriteErrorLine($"Cleaning up failed ({failure}). To free space by hand:", ConsoleStyle.Error);
                await cleanUp.WriteFreeSpaceCommandsAsync(host, policy.BuildCacheKeepArgument).ConfigureAwait(false);
                return 1;
            default:
                // The lock said why it could not be had; nothing was pruned.
                return 1;
        }
    }

    private int PrintPlan(string host, DiskPolicy policy)
    {
        var writer = Console.Out;
        void Line(IReadOnlyList<string> arguments) => writer.WriteLine($"  docker {string.Join(' ', arguments)}");

        writer.WriteLine("Dry run — the following docker commands would run (no changes made):");
        writer.WriteLine();
        writer.WriteLine("  # take the box's deploy lock (waiting while a deploy holds it):");
        Line(BuildLockArguments(host, LockOwner));
        writer.WriteLine("  # remove untagged images (never -a: :previous is the rollback), then trim the build cache (-a: shared records too, and it removes no image):");
        Line(PruneCommands.BuildImagePruneArguments(host));
        Line(PruneCommands.BuildBuilderPruneArguments(host, policy.BuildCacheKeepArgument));
        writer.WriteLine("  # release the box's lock");
        return 0;
    }
}
