using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Auth;

/// <summary>Works through <see cref="PasswordResetQueue" />: each address looked up, and its email queued when it has an account.</summary>
internal sealed partial class PasswordResetSender<TUser, TKey>(
    PasswordResetQueue queue,
    IServiceScopeFactory scopes,
    ILogger<PasswordResetSender<TUser, TKey>> logger) : BackgroundService
    where TUser : Authenticatable<TKey>, new()
    where TKey : notnull
{
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        queue.IsRunning = true;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.IsRunning = false;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await SendAsync(request, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: what was waiting is dropped, and asked for again.
        }
    }

    private async Task SendAsync(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                await scope.ServiceProvider.GetRequiredService<AccountService<TUser, TKey>>()
                    .QueueResetAsync(request.Address, request.Origin, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // One address that fails must not stop the ones behind it; it is logged.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ResetFailed(logger, ex);
        }
        finally
        {
            queue.Done();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A password reset request could not be worked through; its email was not queued.")]
    private static partial void ResetFailed(ILogger logger, Exception exception);
}
