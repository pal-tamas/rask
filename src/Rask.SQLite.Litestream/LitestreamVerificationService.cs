using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Litestream;

/// <summary>
/// Hosted service that verifies the backup is restorable every
/// <see cref="LitestreamVerificationOptions.Interval"/> (and once at startup when
/// <see cref="LitestreamVerificationOptions.VerifyOnStartup"/> is set). Registered only when
/// <see cref="LitestreamVerificationOptions.Enabled"/> is set, because every pass costs a real restore.
/// <para>
/// The verifier reports rather than throws, so this loop exists to schedule it and to make sure nothing
/// escaping it can stop the schedule — a backup problem never takes down the app it protects.
/// </para>
/// </summary>
internal sealed class LitestreamVerificationService : BackgroundService
{
    private readonly LitestreamOptions _options;
    private readonly ISqliteBackupVerifier _verifier;
    private readonly ILogger<LitestreamVerificationService> _logger;

    public LitestreamVerificationService(
        LitestreamOptions options,
        ISqliteBackupVerifier verifier,
        ILogger<LitestreamVerificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _verifier = verifier;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Verification.VerifyOnStartup)
        {
            await TryVerifyAsync(stoppingToken).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(_options.Verification.Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TryVerifyAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task TryVerifyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _verifier.VerifyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-verification — not an error.
        }
#pragma warning disable CA1031 // A failed check must not stop the schedule or crash the app — log and continue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Litestream backup verification threw; will retry on the next interval.");
        }
    }
}
