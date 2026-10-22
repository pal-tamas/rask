namespace Rask.Wire.Tests;

[Collection(nameof(Ambient))]
public sealed class CurrentServiceTests
{
    [Fact]
    public void Outside_any_work_it_says_what_to_do_instead()
    {
        var error = Assert.Throws<InvalidOperationException>(Current.Service<TimeProvider>);

        Assert.StartsWith("Current.Service<TimeProvider>() was called outside any work in progress", error.Message, StringComparison.Ordinal);
        Assert.Contains("Inject TimeProvider in the constructor there", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void In_a_job_s_scope_it_is_that_scope_s_instance()
    {
        var clock = new FakeClock();
        var scope = new OnDemandServicesTests.WaitingApp.Services(clock);

        TimeProvider resolved;
        using (Ambient.Enter(scope))
        {
            resolved = Current.Service<TimeProvider>();
        }

        Assert.Same(clock, resolved);
    }

    [Fact]
    public void A_service_nobody_registered_is_the_standard_no_service_error()
    {
        var scope = new OnDemandServicesTests.WaitingApp.Services(TimeProvider.System);

        InvalidOperationException error;
        using (Ambient.Enter(scope))
        {
            error = Assert.Throws<InvalidOperationException>(Current.Service<FakeClock>);
        }

        Assert.Equal($"No service for type '{typeof(FakeClock)}' has been registered.", error.Message);
    }

    [Fact]
    public void A_test_s_app_waiting_to_be_needed_is_started_by_it()
    {
        var app = new OnDemandServicesTests.WaitingApp();

        TimeProvider resolved;
        using (Ambient.Enter(app))
        {
            resolved = Current.Service<TimeProvider>();
        }

        Assert.Same(app.Clock, resolved);
        Assert.Equal(1, app.Starts);
    }

    [Fact]
    public void The_request_in_progress_answers_when_nothing_was_pushed()
    {
        var request = new OnDemandServicesTests.WaitingApp.Services(new FakeClock());
        var previous = Ambient.RequestServices;
        Ambient.RequestServices = () => request;

        TimeProvider resolved;
        try
        {
            resolved = Current.Service<TimeProvider>();
        }
        finally
        {
            Ambient.RequestServices = previous;
        }

        Assert.IsType<FakeClock>(resolved);
    }

    [Fact]
    public void What_was_pushed_answers_before_the_request()
    {
        var job = new OnDemandServicesTests.WaitingApp.Services(new FakeClock());
        var request = new OnDemandServicesTests.WaitingApp.Services(TimeProvider.System);
        var previous = Ambient.RequestServices;
        Ambient.RequestServices = () => request;

        TimeProvider resolved;
        try
        {
            using (Ambient.Enter(job))
            {
                resolved = Current.Service<TimeProvider>();
            }
        }
        finally
        {
            Ambient.RequestServices = previous;
        }

        Assert.IsType<FakeClock>(resolved);
    }

    private sealed class FakeClock : TimeProvider;
}
