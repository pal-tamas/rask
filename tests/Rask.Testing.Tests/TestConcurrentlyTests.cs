using Rask.Wire;

namespace Rask.Testing.Tests;

public sealed class TestConcurrentlyTests
{
    [Fact]
    public async Task One_through_and_one_refused_hands_back_the_refusal()
    {
        static Task Through() => Task.CompletedTask;

        var refusal = await Test.Concurrently(Through, () => Refuse("Number", "Taken."));

        refusal.Only("Number");
    }

    [Fact]
    public async Task The_two_run_at_the_same_moment()
    {
        using var bothStarted = new Barrier(2);

        Task Meet() => Task.Run(() => Assert.True(bothStarted.SignalAndWait(TimeSpan.FromSeconds(10))));

        var refusal = await Test.Concurrently(Meet, async () =>
        {
            await Meet();
            await Refuse(null, "Someone else changed this in the meantime.");
        });

        refusal.Whole();
    }

    [Fact]
    public async Task Both_through_fails_the_test()
    {
        static Task Through() => Task.CompletedTask;

        var failed = await Assert.ThrowsAsync<RefusalAssertionException>(() => Test.Concurrently(Through, Through));

        Assert.Equal("Expected one to go through and one to be refused; both went through.", failed.Message);
    }

    [Fact]
    public async Task Both_refused_fails_the_test()
    {
        static Task Refused() => Refuse("Name", "Taken.");

        var failed = await Assert.ThrowsAsync<RefusalAssertionException>(() => Test.Concurrently(Refused, Refused));

        Assert.StartsWith("Expected one to go through and one to be refused; both were refused", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_exception_fails_the_test_naming_it()
    {
        static Task Through() => Task.CompletedTask;
        static async Task Broken()
        {
            await Task.Yield();
            throw new InvalidOperationException("The connection broke.");
        }

        var failed = await Assert.ThrowsAsync<RefusalAssertionException>(() => Test.Concurrently(Broken, Through));

        Assert.Equal(
            "Expected one to go through and one to be refused; one threw InvalidOperationException: The connection broke.",
            failed.Message);
        Assert.IsType<InvalidOperationException>(failed.InnerException);
    }

    private static async Task Refuse(string? field, string message)
    {
        await Task.Yield();
        throw new RefusedException([new FieldFailure(message, field is null ? [] : [field])]);
    }

    private sealed class RefusedException(IReadOnlyList<FieldFailure> failures) : Exception("Refused."), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = failures;
    }
}
