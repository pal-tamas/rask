namespace Rask.Cli.Commands;

/// <summary>
/// Sends a deploy's <c>docker -H ssh://&lt;host&gt; …</c> calls through <see cref="Tunnel"/> while it is open — the same
/// call, addressed to the forwarded socket instead of a fresh ssh connection. Everything else, and every call once the
/// tunnel has dropped, goes as it was written, so a lost tunnel costs speed, never a step.
/// </summary>
internal sealed class TunnelRunner(IProcessRunner inner) : IProcessRunner
{
    /// <summary>The tunnel calls to its host go through, or <c>null</c> for none.</summary>
    public DockerTunnel? Tunnel { get; set; }

    public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.RunAsync(fileName, Route(fileName, arguments), workingDirectory, cancellationToken, environment);

    public Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.CaptureAsync(fileName, Route(fileName, arguments), workingDirectory, cancellationToken, environment);

    public Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.RunTeeAsync(fileName, Route(fileName, arguments), workingDirectory, onLine, cancellationToken, environment);

    public Task<int> PipeAsync(string fileName, IReadOnlyList<string> source, IReadOnlyList<string> sink, string? workingDirectory, CancellationToken cancellationToken) =>
        inner.PipeAsync(fileName, Route(fileName, source), Route(fileName, sink), workingDirectory, cancellationToken);

    private IReadOnlyList<string> Route(string fileName, IReadOnlyList<string> arguments) =>
        Tunnel is { IsOpen: true } tunnel
            && string.Equals(fileName, "docker", StringComparison.Ordinal)
            && arguments.Count > 1
            && string.Equals(arguments[0], "-H", StringComparison.Ordinal)
            && string.Equals(arguments[1], tunnel.Replaces, StringComparison.Ordinal)
            ? ["-H", tunnel.Endpoint, .. arguments.Skip(2)]
            : arguments;
}
