namespace Rask.Meta.Hosting;

/// <summary>
///     Configures the supervised Node process and the forwarder in front of it.
/// </summary>
public sealed class MetaHostingOptions
{
    /// <summary>Which framework's build output is being hosted.</summary>
    public MetaFramework Framework { get; set; } = MetaFramework.TanStackStart;

    /// <summary>
    ///     The directory holding the framework's build output. Relative paths resolve against the
    ///     content root.
    /// </summary>
    public string AppDirectory { get; set; } = "Client";

    /// <summary>The <c>node</c> executable, resolved on <c>PATH</c> unless given a full path.</summary>
    public string NodeExecutable { get; set; } = "node";

    /// <summary>The loopback port the Node process is told to listen on.</summary>
    /// <remarks>
    ///     A fixed default rather than an ephemeral port picked by binding to 0. Picking one means
    ///     binding a socket, reading the port, closing it and handing the number to a process that
    ///     binds it a moment later — a race with anything else on the machine doing the same. Inside a
    ///     container, which is where this runs, there is nothing to collide with.
    /// </remarks>
    public int Port { get; set; } = 3000;

    /// <summary>
    ///     How long the Node process has to accept a connection before a start attempt is abandoned.
    /// </summary>
    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     How long the Node process is given to exit on <c>SIGTERM</c> before its tree is killed.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     How many times a crashed Node process is restarted before the whole application is stopped.
    /// </summary>
    /// <remarks>
    ///     Exhausting this budget stops the host rather than serving 502s for ever. An orchestrator
    ///     restarting the container — fresh filesystem, fresh memory, and an event someone can see —
    ///     is a better supervisor than one written here, and a hard exit is louder than a degraded
    ///     process that still answers health checks.
    /// </remarks>
    public int MaxRestartAttempts { get; set; } = 5;

    /// <summary>
    ///     How long a run must last to count as a recovery, resetting the restart budget.
    /// </summary>
    /// <remarks>
    ///     Without this the budget would be a <em>lifetime</em> one, and a server that runs happily for
    ///     weeks would still take the host down on its fifth crash however far apart those were. What
    ///     <see cref="MaxRestartAttempts" /> is meant to catch is a process that will not stay running
    ///     — consecutive failures — not the ordinary attrition of a long-lived one.
    /// </remarks>
    public TimeSpan HealthyRunThreshold { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The base URL handed to the Node process so server-side rendering can call back into this
    ///     app, under the variable named by <see cref="BaseUrlVariable" />.
    /// </summary>
    /// <remarks>
    ///     Injected by the host rather than configured in the front end, so it cannot drift from where
    ///     the app is actually listening. It is also never derived from a request header: server-side
    ///     rendering calls back carrying the visitor's own cookie, so a destination an attacker can
    ///     influence turns that into a confused deputy.
    /// </remarks>
    public string? BaseUrl { get; set; }

    /// <summary>The environment variable carrying <see cref="BaseUrl" />.</summary>
    public string BaseUrlVariable { get; set; } = "RASK_BASE_URL";

    /// <summary>Extra environment variables for the Node process.</summary>
    public IDictionary<string, string> Environment { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    ///     Whether to supervise a Node process at all. <c>false</c> forwards to an already-running
    ///     server on <see cref="Port" />.
    /// </summary>
    /// <remarks>
    ///     The escape hatch for running the front end yourself — under a debugger, or as a second
    ///     container behind the same proxy. The forwarder does not care what is listening.
    /// </remarks>
    public bool SuperviseNode { get; set; } = true;
}
