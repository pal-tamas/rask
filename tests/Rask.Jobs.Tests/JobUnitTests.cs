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
        var options = new JobOptions
        {
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromHours(1),
        };

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), options.RetryDelay(attempts));
    }

    [Fact]
    public void AddRecurring_rejects_a_non_positive_interval()
    {
        var options = new JobOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            options.AddRecurring<TickJob>("tick", TimeSpan.Zero, () => new TickJob()));
    }

    [Fact]
    public void AddRecurring_rejects_a_duplicate_name()
    {
        var options = new JobOptions();
        options.AddRecurring<TickJob>("tick", TimeSpan.FromHours(1), () => new TickJob());
        Assert.Throws<ArgumentException>(() =>
            options.AddRecurring<TickJob>("tick", TimeSpan.FromHours(2), () => new TickJob()));
    }
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
}
