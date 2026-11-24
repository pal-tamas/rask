using Rask.Wire;

namespace Rask.Testing.Tests;

public sealed class TestRefusesTests
{
    [Fact]
    public void A_refusal_at_the_field_asked_about_passes_and_chains()
    {
        var refusal = Test.Refuses(() => Throw(Refused(("Km.End", "End before start."), ("Number", "Taken."))));

        var same = refusal.At("Km.End").At("Number");

        Assert.Same(refusal, same);
        Assert.Equal(2, refusal.Failures.Count);
    }

    [Fact]
    public async Task An_awaited_operation_that_is_refused_hands_back_its_refusal()
    {
        static async Task Rename()
        {
            await Task.Yield();
            throw Refused(("Name", "Required."));
        }

        var refusal = await Test.Refuses(Rename);

        refusal.Only("Name");
    }

    [Fact]
    public void A_refusal_elsewhere_names_every_field_it_was_refused_at()
    {
        var refusal = Test.Refuses(() => Throw(Refused(("Km.Start", "Start after end."), (null, "Locked."))));

        var failed = Assert.Throws<RefusalAssertionException>(() => refusal.At("Km.End"));

        Assert.Equal(
            "Expected a refusal at Km.End; refused at Km.Start (\"Start after end.\"); the whole form (\"Locked.\").",
            failed.Message);
    }

    [Fact]
    public void Only_fails_when_another_field_is_refused_too()
    {
        var refusal = Test.Refuses(() => Throw(Refused(("Number", "Taken."), ("Year", "Closed."))));

        var failed = Assert.Throws<RefusalAssertionException>(() => refusal.Only("Number"));

        Assert.StartsWith("Expected a refusal at Number alone;", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Whole_passes_only_for_a_failure_under_no_field()
    {
        var whole = Test.Refuses(() => Throw(Refused((null, "No place to write from."))));
        var atField = Test.Refuses(() => Throw(Refused(("Name", "Required."))));

        whole.Whole();
        var failed = Assert.Throws<RefusalAssertionException>(() => atField.Whole());

        Assert.Equal("Expected a refusal of the whole form; refused at Name (\"Required.\").", failed.Message);
    }

    [Fact]
    public void An_operation_that_goes_through_fails_the_test()
    {
        var failed = Assert.Throws<RefusalAssertionException>(() => Test.Refuses(() => { }));

        Assert.Equal("Expected a refusal; it went through.", failed.Message);
    }

    [Fact]
    public void Another_exception_fails_the_test_and_is_kept_as_the_cause()
    {
        var boom = new InvalidOperationException("Boom.");

        var failed = Assert.Throws<RefusalAssertionException>(() => Test.Refuses(() => Throw(boom)));

        Assert.Equal("Expected a refusal; it threw InvalidOperationException: Boom.", failed.Message);
        Assert.Same(boom, failed.InnerException);
    }

    // A void method, so the lambda that calls it is an Action and never also a Func<Task>, as a throw-lambda would be.
    private static void Throw(Exception refused) => throw refused;

    private static RefusedException Refused(params (string? Field, string Message)[] failures) =>
        new([.. failures.Select(f => new FieldFailure(f.Message, f.Field is null ? [] : [f.Field]))]);

    private sealed class RefusedException(IReadOnlyList<FieldFailure> failures) : Exception("Refused."), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = failures;
    }
}
