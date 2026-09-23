namespace Rask.Jobs.Tests;

public sealed class JobOptionsTests
{
    [Theory]
    [InlineData(1, 10)]      // base delay
    [InlineData(2, 20)]      // ×2
    [InlineData(3, 40)]      // ×4
    [InlineData(5, 160)]     // ×16
    [InlineData(100, 3600)]  // capped at MaxRetryDelay (1h)
    public void RetryDelay_is_exponential_and_capped(int attempts, double expectedSeconds)
    {
        var options = new JobsOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromHours(1),
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.RetryDelay(attempts));
    }

    [Fact]
    public void A_recurring_interval_of_zero_is_refused()
    {
        var options = new JobsOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            options.Run(() => new TickJob()).Named("tick").Every(TimeSpan.Zero));
    }

    [Fact]
    public void Two_schedules_under_one_name_are_refused()
    {
        var options = new JobsOptions();
        options.Run(() => new TickJob()).Named("tick").Every(TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() =>
            options.Run(() => new TickJob()).Named("tick").Every(TimeSpan.FromHours(2)));
    }

    [Fact]
    public void RecurringJobs_exposes_the_registered_schedule()
    {
        var options = new JobsOptions();
        options.Run(() => new TickJob()).Named("tick").Every(TimeSpan.FromMinutes(5));
        options.Run(() => new RecordJob("digest")).Named("digest").Every(TimeSpan.FromHours(24));

        // The schedule an operator surface reads: registration order, durable name, cadence.
        Assert.Collection(
            options.RecurringJobs,
            r =>
            {
                Assert.Equal("tick", r.Name);
                Assert.Equal("every 5m", r.Schedule?.ToString());
            },
            r =>
            {
                Assert.Equal("digest", r.Name);
                Assert.Equal("every 1d", r.Schedule?.ToString());
            });
        // The factory is reachable, so a caller can enqueue an off-schedule run of a recurring job.
        Assert.IsType<TickJob>(options.RecurringJobs[0].Factory());
    }

    [Fact]
    public void RecurringJobs_is_empty_when_nothing_is_registered()
    {
        Assert.Empty(new JobsOptions().RecurringJobs);
    }

    [Fact]
    public void Validate_rejects_a_non_positive_poll_interval()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobsOptions { PollInterval = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void Validate_rejects_a_max_retry_delay_below_the_base()
    {
        var options = new JobsOptions { BaseRetryDelay = TimeSpan.FromMinutes(5), MaxRetryDelay = TimeSpan.FromMinutes(1) };

        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }
}

// A nested IJob: its Type.FullName uses '+', which must still match the generator's dotted registration.
public sealed class Outer
{
    public sealed record NestedJob(int N) : IJob;
}

public sealed class JobSerializerRegistryTests
{
    [Fact]
    public void Serialize_then_deserialize_round_trips_a_job()
    {
        // The Rask.Jobs source generator registered this assembly's IJob types at module load.
        var (type, payload) = JobSerializerRegistry.Serialize(new RecordJob("payload"));

        var back = JobSerializerRegistry.Deserialize(type, payload);

        var job = Assert.IsType<RecordJob>(back);
        Assert.Equal("payload", job.Value);
    }

    [Fact]
    public void Deserialize_returns_null_for_an_unregistered_type()
    {
        Assert.Null(JobSerializerRegistry.Deserialize("Nope.NotAJob", "{}"));
    }

    [Fact]
    public void A_nested_job_type_round_trips()
    {
        var (type, payload) = JobSerializerRegistry.Serialize(new Outer.NestedJob(7));

        Assert.DoesNotContain('+', type); // stored dotted, matching the generator's registration

        var back = JobSerializerRegistry.Deserialize(type, payload);
        Assert.Equal(7, Assert.IsType<Outer.NestedJob>(back).N);
    }
}
