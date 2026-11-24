using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask;

/// <summary>
///     Hands the host's <see cref="ISecrets" /> to what runs outside any work — a seeder at start, a migration's data
///     step — so a <c>Secret&lt;T&gt;</c> column written there is encrypted with the same keys as one written by a page.
/// </summary>
internal sealed class SecretsAtStart(ISecrets secrets) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        SecretKeeping.Default ??= secrets;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (ReferenceEquals(SecretKeeping.Default, secrets))
        {
            SecretKeeping.Default = null;
        }

        return Task.CompletedTask;
    }
}
