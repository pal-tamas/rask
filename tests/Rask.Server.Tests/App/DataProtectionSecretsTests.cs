using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>A server host encrypts a <c>Secret&lt;T&gt;</c> column with its Data Protection key ring, and hands it to what runs at start.</summary>
public sealed class DataProtectionSecretsTests
{
    private static ServiceProvider Host()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        DataHostSeams.Add(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_server_host_keeps_a_secret_as_a_ciphertext_its_key_ring_reads_back()
    {
        using var host = Host();
        var secrets = host.GetRequiredService<ISecrets>();

        var stored = secrets.Protect("k-123");

        Assert.NotEqual("k-123", stored);
        Assert.DoesNotContain("k-123", stored, StringComparison.Ordinal);
        Assert.Equal("k-123", secrets.Unprotect(stored));
    }

    [Fact]
    public async Task The_host_hands_its_secrets_to_what_runs_outside_any_work_while_it_runs()
    {
        var before = SecretKeeping.Default;
        SecretKeeping.Default = null;
        using var host = Host();
        var start = host.GetServices<IHostedService>().OfType<SecretsAtStart>().Single();

        await start.StartAsync(TestContext.Current.CancellationToken);
        var running = SecretKeeping.Default;
        await start.StopAsync(TestContext.Current.CancellationToken);
        var stopped = SecretKeeping.Default;
        SecretKeeping.Default = before;

        Assert.Same(host.GetRequiredService<ISecrets>(), running);
        Assert.Null(stopped);
    }
}
