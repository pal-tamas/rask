using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamVerificationServiceTests
{
    [Fact]
    public async Task Verification_repeats_on_the_interval()
    {
        var verifier = new CountingVerifier(target: 3);
        await using var provider = NewProvider(verifier, o =>
        {
            o.Verification.Enabled = true;
            o.Verification.Interval = TimeSpan.FromMilliseconds(20);   // spin fast for the test
        });
        var service = VerificationService(provider);

        await service.StartAsync(CancellationToken.None);
        await verifier.ReachedTarget.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(verifier.CallCount >= 3, $"expected repeats, got {verifier.CallCount} pass(es).");
    }

    [Fact]
    public async Task Verification_can_run_once_at_startup()
    {
        var verifier = new CountingVerifier(target: 1);
        await using var provider = NewProvider(verifier, o =>
        {
            o.Verification.Enabled = true;
            o.Verification.VerifyOnStartup = true;
            o.Verification.Interval = TimeSpan.FromHours(24);   // only startup can satisfy the target
        });
        var service = VerificationService(provider);

        await service.StartAsync(CancellationToken.None);
        await verifier.ReachedTarget.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, verifier.CallCount);
    }

    [Fact]
    public async Task A_throwing_verifier_does_not_stop_the_schedule()
    {
        // The verifier reports rather than throws, but a bug there must not silently end the schedule and
        // leave the backup unverified for the life of the process.
        var verifier = new CountingVerifier(target: 3) { Throw = true };
        await using var provider = NewProvider(verifier, o =>
        {
            o.Verification.Enabled = true;
            o.Verification.Interval = TimeSpan.FromMilliseconds(20);
        });
        var service = VerificationService(provider);

        await service.StartAsync(CancellationToken.None);
        await verifier.ReachedTarget.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(verifier.CallCount >= 3, $"the schedule stopped after {verifier.CallCount} pass(es).");
    }

    private static ServiceProvider NewProvider(ISqliteBackupVerifier verifier, Action<LitestreamOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(verifier);   // wins over the default (TryAddSingleton)
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/tmp/rask-litestream-verify-service.db";
            o.ReplicaUrl = "file:///tmp/rask-litestream-verify-service";
            configure(o);
        });

        return services.BuildServiceProvider();
    }

    private static IHostedService VerificationService(ServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<LitestreamVerificationService>().Single();

    private sealed class CountingVerifier(int target) : ISqliteBackupVerifier
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public bool Throw { get; init; }

        public int CallCount => Volatile.Read(ref _calls);

        public Task ReachedTarget => _reached.Task;

        public Task<LitestreamVerificationStatus> VerifyAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) >= target)
            {
                _reached.TrySetResult();
            }

            return Throw
                ? throw new InvalidOperationException("boom")
                : Task.FromResult(new LitestreamVerificationStatus(
                    LitestreamVerificationOutcome.Verified,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch,
                    TimeSpan.Zero,
                    null));
        }
    }
}
