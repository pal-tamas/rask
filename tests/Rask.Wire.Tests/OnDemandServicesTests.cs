namespace Rask.Wire.Tests;

public sealed class OnDemandServicesTests
{
    [Fact]
    public void A_lookup_never_starts_an_app_waiting_to_be_needed()
    {
        var app = new WaitingApp();

        using (Ambient.Enter(app))
        {
            Assert.Null(Ambient.Services);
        }

        Assert.Equal(0, app.Starts);
    }

    [Fact]
    public void A_call_with_nothing_else_to_go_on_starts_it_once()
    {
        var app = new WaitingApp();

        using (Ambient.Enter(app))
        {
            Assert.Same(app.Provider, Ambient.Needed);
            Assert.Same(app.Provider, Ambient.Needed);
            Assert.Same(app.Provider, Ambient.Services);
        }

        Assert.Equal(1, app.Starts);
    }

    [Fact]
    public void A_battery_reach_starts_it()
    {
        var app = new WaitingApp();

        using (Ambient.Enter(app))
        {
            Assert.Same(app.Clock, Ambient.Reach<TimeProvider>("Clock", "never", "AddClock()"));
        }

        Assert.Equal(1, app.Starts);
    }

    [Fact]
    public void Once_the_test_sets_up_its_own_database_nothing_starts_it()
    {
        var app = new WaitingApp();

        using (Ambient.Enter(app))
        {
            Ambient.DeclineTestApp();

            Assert.Null(Ambient.Needed);
            Assert.Throws<InvalidOperationException>(() => Ambient.Reach<TimeProvider>("Clock", "never", "AddClock()"));
        }

        Assert.Equal(0, app.Starts);
    }

    [Fact]
    public void An_app_already_started_is_not_stood_down()
    {
        var app = new WaitingApp();

        using (Ambient.Enter(app))
        {
            _ = Ambient.Needed;
            Ambient.DeclineTestApp();

            Assert.Same(app.Provider, Ambient.Services);
        }

        Assert.Equal(1, app.Starts);
    }

    [Fact]
    public void Work_in_progress_answers_before_a_waiting_app()
    {
        var app = new WaitingApp();
        var work = new WaitingApp.Services(TimeProvider.System);
        var previous = Ambient.FallbackServices;
        Ambient.FallbackServices = () => work;

        try
        {
            using (Ambient.Enter(app))
            {
                Assert.Same(work, Ambient.Needed);
            }
        }
        finally
        {
            Ambient.FallbackServices = previous;
        }

        Assert.Equal(0, app.Starts);
    }

    private sealed class WaitingApp : OnDemandServices
    {
        private IServiceProvider? _started;

        public TimeProvider Clock { get; } = TimeProvider.System;

        public IServiceProvider Provider => _started ??= new Services(Clock);

        public int Starts { get; private set; }

        internal override IServiceProvider? Started => Starts == 0 ? null : Provider;

        protected override IServiceProvider? StartNow()
        {
            Starts++;
            return Provider;
        }

        internal sealed class Services(TimeProvider clock) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(TimeProvider) ? clock : null;
        }
    }
}
