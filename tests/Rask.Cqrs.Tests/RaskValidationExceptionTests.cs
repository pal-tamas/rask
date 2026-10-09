using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Cqrs.Tests;

/// <summary>
/// A rejected request names the fields it is about (<see cref="IFieldFailures" />), which is what lets a form
/// show each message under its field — and it keeps the <c>Errors</c> dictionary that crosses the wire.
/// </summary>
public sealed class RaskValidationExceptionTests
{
    [Fact]
    public void A_validators_errors_become_one_failure_each_under_the_field_it_named()
    {
        var rejected = new RaskValidationException(
        [
            new RequestValidationError("Quantity", "Quantity must be at least 1."),
            new RequestValidationError("Lines[2].ValidFrom", "Starts before the order."),
        ]);

        var failures = rejected.Failures;

        Assert.Equal(
            [("Quantity must be at least 1.", "Quantity"), ("Starts before the order.", "Lines[2].ValidFrom")],
            failures.Select(f => (f.Message, f.Fields.Single())));
        Assert.All(failures, f => Assert.Null(f.Marked));
    }

    [Fact]
    public void A_rule_about_the_request_as_a_whole_is_a_failure_under_no_field()
    {
        var rejected = new RaskValidationException([new RequestValidationError("", "The request as a whole is wrong.")]);

        var failure = Assert.Single(rejected.Failures);

        Assert.Empty(failure.Fields);
        Assert.Equal(["The request as a whole is wrong."], rejected.Errors[""]);
    }

    [Fact]
    public void One_failure_over_several_fields_stays_one_and_the_wire_shape_repeats_it_under_each()
    {
        var taken = new FieldFailure("That invoice number is taken.", ["Year", "Number"], Source: "IX_Invoice_Year_Number");

        var rejected = new RaskValidationException([taken]);

        Assert.Same(taken, Assert.Single(rejected.Failures));
        Assert.Equal(["That invoice number is taken."], rejected.Errors["Year"]);
        Assert.Equal(["That invoice number is taken."], rejected.Errors["Number"]);
        Assert.Equal(2, rejected.Errors.Count);
        Assert.Equal("The request failed validation: Year, Number.", rejected.Message);
    }

    [Fact]
    public void The_exception_behind_a_failure_is_kept_for_the_log()
    {
        var refusal = new InvalidOperationException("duplicate key");

        var rejected = new RaskValidationException([new FieldFailure("Taken.", ["Name"])], refusal);

        Assert.Same(refusal, rejected.InnerException);
    }

    [Theory]
    [InlineData("Nothing in particular.")]
    [InlineData("The request failed validation.")]
    public void An_exception_that_names_no_field_still_says_something_about_the_submission(string message)
    {
        var rejected = new RaskValidationException(message);

        var failure = Assert.Single(rejected.Failures);

        // Never empty: a form reads "no failures" as "nothing wrong" and would swallow the exception.
        Assert.Equal(message, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Empty(rejected.Errors);
    }

    [Fact]
    public void An_empty_list_of_failures_is_not_nothing_wrong_either()
    {
        var rejected = new RaskValidationException(Array.Empty<FieldFailure>());

        var failure = Assert.Single(rejected.Failures);

        Assert.Empty(failure.Fields);
    }

    [Fact]
    public async Task A_request_the_pipeline_rejects_carries_its_validators_fields_as_failures()
    {
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddSingleton<IRequestValidator<Add>, NoNegatives>();
        await using var sp = services.BuildServiceProvider();

        var rejected = await Assert.ThrowsAsync<RaskValidationException>(
            () => sp.GetRequiredService<IDispatcher>().Query(new Add(-1, 3), TestContext.Current.CancellationToken));

        var failure = Assert.Single(rejected.Failures);
        Assert.Equal(("A must not be negative.", "A"), (failure.Message, failure.Fields.Single()));
    }

    private sealed class NoNegatives : IRequestValidator<Add>
    {
        public ValueTask<IReadOnlyList<RequestValidationError>> Validate(Add request) =>
            ValueTask.FromResult<IReadOnlyList<RequestValidationError>>(
                request.A < 0 ? [new RequestValidationError("A", "A must not be negative.")] : []);
    }
}
