using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRaskSqliteLitestream_registers_restorer_executor_and_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/data/app.db";
            o.ReplicaUrl = "s3://bucket/app";
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<LitestreamRestorer>());
        Assert.NotNull(provider.GetService<ILitestreamExecutor>());
        Assert.Single(services, d => d.ImplementationType == typeof(LitestreamReplicationService));
    }

    [Fact]
    public void AddRaskSqliteLitestream_registers_the_verifier_even_with_the_schedule_off()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/data/app.db";
            o.ReplicaUrl = "s3://bucket/app";
        });

        using var provider = services.BuildServiceProvider();

        // On-demand verification costs nothing until someone calls it, so it is always available; only the
        // recurring restore — which spends egress on its own — is gated.
        Assert.NotNull(provider.GetService<ISqliteBackupVerifier>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_verification_schedule_is_registered_either_way_and_decides_when_it_starts(bool enabled)
    {
        // Verification.Enabled can come from Rask:Litestream:Verification, which is not readable while services are
        // being registered, so the service is always there and returns at once when the schedule is off —
        // LitestreamVerificationServiceTests pins that it then never restores anything.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/data/app.db";
            o.ReplicaUrl = "s3://bucket/app";
            o.Verification.Enabled = enabled;
        });

        Assert.Single(services, d => d.ImplementationType == typeof(LitestreamVerificationService));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IHostedService)));
    }

    [Fact]
    public void AddRaskSqliteLitestream_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskSqliteLitestream(o => { o.DatabasePath = "/a.db"; o.ReplicaUrl = "s3://b/a"; });
        services.AddRaskSqliteLitestream(o => { o.DatabasePath = "/c.db"; o.ReplicaUrl = "s3://d/c"; });

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IHostedService)));

        using var provider = services.BuildServiceProvider();
        Assert.Equal("/a.db", provider.GetRequiredService<LitestreamOptions>().DatabasePath);
    }

    [Fact]
    public void AddRaskSqliteLitestream_validates_options()
    {
        // Refused when the options are built — at host start, or on the first resolve here — naming the section.
        var services = new ServiceCollection();
        services.AddRaskSqliteLitestream(o => o.DatabasePath = "/data/app.db"); // no replica / config
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<LitestreamOptions>());
        Assert.Contains("Rask:Litestream", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestoreSqliteFromLitestreamAsync_throws_when_not_registered()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.RestoreSqliteFromLitestreamAsync());
    }
}
