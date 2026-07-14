using CliWrap;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Litestream;

/// <summary>
/// Default <see cref="ILitestreamExecutor"/>: runs the configured <c>litestream</c> executable via
/// <see href="https://github.com/Tyrrrz/CliWrap">CliWrap</see>, piping stdout/stderr to the logger.
/// Exit-code validation is disabled so callers inspect the code themselves; cancelling the token kills
/// the process (a time-boxed restore, or a graceful shutdown of the long-running <c>replicate</c>).
/// </summary>
internal sealed class CliWrapLitestreamExecutor : ILitestreamExecutor
{
    private readonly LitestreamOptions _options;
    private readonly ILogger<CliWrapLitestreamExecutor> _logger;

    public CliWrapLitestreamExecutor(LitestreamOptions options, ILogger<CliWrapLitestreamExecutor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var command = Cli.Wrap(LitestreamExecutableResolver.Resolve(_options.ExecutablePath))
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line => _logger.LogInformation("litestream: {Line}", line)))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line => _logger.LogWarning("litestream: {Line}", line)));

        // Graceful stop: on cancellation send an interrupt (SIGINT) so litestream flushes its final WAL
        // frames, then force-kill only if it hasn't exited within the grace period. On a platform that
        // recycles the process with a SIGTERM (App Service, Kubernetes) this replicates the last writes
        // instead of dropping them.
        using var forcefulCts = new CancellationTokenSource();
        using var registration = cancellationToken.Register(() => forcefulCts.CancelAfter(_options.ShutdownGracePeriod));

        var result = await command.ExecuteAsync(forcefulCts.Token, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }
}
