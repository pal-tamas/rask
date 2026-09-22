using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Wasm.Tests.Hosting;

public class WasmHostedServicesTests
{
    [Fact]
    public async Task Starting_starts_every_registered_hosted_service()
    {
        var first = new RecordingHostedService("first");
        var second = new RecordingHostedService("second");
        var host = Build(first, second);

        await host.StartAsync();

        Assert.True(first.Started);
        Assert.True(second.Started);
    }

    [Fact]
    public async Task Starting_with_no_hosted_services_does_nothing()
    {
        var host = new WasmHostedServices(new ServiceCollection().BuildServiceProvider());

        await host.StartAsync();

        Assert.Empty(host.Started);
    }

    [Fact]
    public async Task Starting_goes_in_registration_order()
    {
        var log = new List<string>();
        var host = Build(
            new RecordingHostedService("first", log),
            new RecordingHostedService("second", log),
            new RecordingHostedService("third", log));

        await host.StartAsync();

        Assert.Equal(["start:first", "start:second", "start:third"], log);
    }

    // The reason this host swallows instead of propagating: in a browser tab there is no orchestrator to
    // restart the process, so a failed background worker must degrade the app, not blank it.
    [Fact]
    public async Task A_service_that_throws_on_start_still_lets_the_rest_start()
    {
        var throwing = new ThrowingHostedService(onStart: true);
        var after = new RecordingHostedService("after");
        var host = Build(throwing, after);

        await host.StartAsync();

        Assert.True(after.Started);
    }

    [Fact]
    public async Task A_service_that_throws_on_start_is_not_recorded_as_started()
    {
        var throwing = new ThrowingHostedService(onStart: true);
        var after = new RecordingHostedService("after");
        var host = Build(throwing, after);

        await host.StartAsync();

        // Not merely cosmetic: stopping a BackgroundService that never entered ExecuteAsync would hand
        // it a stop signal for work it never began.
        Assert.DoesNotContain(throwing, host.Started);
        Assert.Equal([after], host.Started);
    }

    // Resolving IEnumerable<IHostedService> constructs every one of them, so a throwing constructor faults
    // at the resolve, not at StartAsync. Left unguarded it escapes RunAsync and blanks the app — the exact
    // outcome the per-service catch exists to prevent.
    [Fact]
    public async Task A_constructor_that_throws_on_start_does_not_propagate()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IHostedService, ThrowingConstructorService>();
        var host = new WasmHostedServices(collection.BuildServiceProvider());

        await host.StartAsync();

        Assert.Empty(host.Started);
    }

    [Fact]
    public async Task An_unregistered_dependency_on_start_does_not_propagate()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<IHostedService, NeedsMissingDependencyService>();
        var host = new WasmHostedServices(collection.BuildServiceProvider());

        await host.StartAsync();

        Assert.Empty(host.Started);
    }

    // A faulting ExecuteAsync must not go unobserved — otherwise a crashed loop is indistinguishable from
    // one that never started, which is the failure this whole class exists to remove.
    [Fact]
    public async Task A_background_service_loop_that_faults_after_start_is_observed()
    {
        var service = new FaultingBackgroundService();
        var host = Build(service);

        await host.StartAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => service.ExecuteTask!);

        // Observed: an unobserved faulted Task would otherwise surface only at finalization.
        Assert.True(service.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Stopping_goes_in_reverse_start_order()
    {
        var log = new List<string>();
        var host = Build(
            new RecordingHostedService("first", log),
            new RecordingHostedService("second", log),
            new RecordingHostedService("third", log));

        await host.StartAsync();
        log.Clear();
        await host.StopAsync(TimeSpan.FromSeconds(1));

        // Reverse, so a service still has anything registered before it while it shuts down.
        Assert.Equal(["stop:third", "stop:second", "stop:first"], log);
    }

    [Fact]
    public async Task Stopping_skips_a_service_that_failed_to_start()
    {
        var throwing = new ThrowingHostedService(onStart: true);
        var host = Build(throwing);

        await host.StartAsync();
        await host.StopAsync(TimeSpan.FromSeconds(1));

        Assert.False(throwing.Stopped);
    }

    // pagehide can fire twice in a tab's life — a bfcache suspend, then the real teardown.
    [Fact]
    public async Task Stopping_twice_stops_only_once()
    {
        var service = new RecordingHostedService("only");
        var host = Build(service);

        await host.StartAsync();
        await host.StopAsync(TimeSpan.FromSeconds(1));
        await host.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public async Task A_service_that_throws_on_stop_still_lets_the_rest_stop()
    {
        var throwing = new ThrowingHostedService(onStart: false);
        var earlier = new RecordingHostedService("earlier");
        var host = Build(earlier, throwing);

        await host.StartAsync();
        await host.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(1, earlier.StopCount);
    }

    [Fact]
    public async Task Stopping_without_a_start_does_nothing()
    {
        var service = new RecordingHostedService("never-started");
        var host = Build(service);

        await host.StopAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(0, service.StopCount);
    }

    // The grace is a deadline handed to the service, not a wall this host enforces — a service that
    // ignores its token still runs to completion, exactly as on the server.
    [Fact]
    public async Task Stopping_passes_a_cancellable_deadline_to_the_service()
    {
        var service = new TokenCapturingHostedService();
        var host = Build(service);

        await host.StartAsync();
        await host.StopAsync(TimeSpan.FromMilliseconds(50));

        Assert.True(service.StopToken.CanBeCanceled);
    }

    private static WasmHostedServices Build(params IHostedService[] services)
    {
        var collection = new ServiceCollection();
        foreach (var service in services)
        {
            collection.AddSingleton(service);
        }

        return new WasmHostedServices(collection.BuildServiceProvider());
    }

    private sealed class RecordingHostedService(string name, List<string>? log = null) : IHostedService
    {
        public bool Started { get; private set; }
        public int StopCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            log?.Add($"start:{name}");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            log?.Add($"stop:{name}");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHostedService(bool onStart) : IHostedService
    {
        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) =>
            onStart ? throw new InvalidOperationException("boom") : Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class ThrowingConstructorService : IHostedService
    {
        public ThrowingConstructorService() => throw new InvalidOperationException("boom");

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NeedsMissingDependencyService(IDisposable neverRegistered) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.FromResult(neverRegistered);
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FaultingBackgroundService : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("loop died");
        }
    }

    private sealed class TokenCapturingHostedService : IHostedService
    {
        public CancellationToken StopToken { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
