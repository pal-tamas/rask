using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

public sealed class ValidationBehaviorTests
{
    private static ServiceProvider Build(
        Action<CqrsOptions>? configure = null,
        params IRequestValidator<Add>[] validators)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Recorder());
        foreach (var validator in validators)
        {
            services.AddSingleton(validator);
        }

        services.AddRaskCqrs(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_valid_request_reaches_its_handler()
    {
        await using var sp = Build(validators: new Rejects());

        var result = await sp.GetRequiredService<IDispatcher>().QueryAsync(new Add(2, 3));

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task An_invalid_request_never_reaches_its_handler()
    {
        await using var sp = Build(validators: new Rejects(when: -1));

        var ex = await Assert.ThrowsAsync<RaskValidationException>(
            () => sp.GetRequiredService<IDispatcher>().QueryAsync(new Add(-1, 3)));

        Assert.Equal(["A must not be negative."], ex.Errors["A"]);
    }

    [Fact]
    public async Task Every_validator_runs_so_the_caller_sees_the_whole_list()
    {
        // Deliberately not first-error-wins. A form gates per field because the user is typing into it;
        // a caller fixing a request wants every problem at once rather than one per round trip.
        await using var sp = Build(validators: [new Rejects(when: -1), new AlsoRejects()]);

        var ex = await Assert.ThrowsAsync<RaskValidationException>(
            () => sp.GetRequiredService<IDispatcher>().QueryAsync(new Add(-1, 3)));

        Assert.Equal(["A must not be negative."], ex.Errors["A"]);
        Assert.Equal(["B is suspicious."], ex.Errors["B"]);
    }

    [Fact]
    public async Task Errors_with_no_field_land_on_the_empty_key()
    {
        await using var sp = Build(validators: new RejectsWholeRequest());

        var ex = await Assert.ThrowsAsync<RaskValidationException>(
            () => sp.GetRequiredService<IDispatcher>().QueryAsync(new Add(1, 1)));

        Assert.Equal(["The request as a whole is wrong."], ex.Errors[string.Empty]);
    }

    [Fact]
    public async Task Validation_off_lets_an_invalid_request_through()
    {
        await using var sp = Build(o => o.ValidateRequests = false, new Rejects(when: -1));

        var result = await sp.GetRequiredService<IDispatcher>().QueryAsync(new Add(-1, 3));

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task With_no_validators_registered_dispatch_is_unchanged()
    {
        await using var sp = Build();

        Assert.Equal(5, await sp.GetRequiredService<IDispatcher>().QueryAsync(new Add(2, 3)));
    }

    private sealed class Rejects(int when = int.MinValue) : IRequestValidator<Add>
    {
        public ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
            Add request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RequestValidationError>>(
                request.A == when
                    ? [new RequestValidationError("A", "A must not be negative.")]
                    : []);
    }

    private sealed class AlsoRejects : IRequestValidator<Add>
    {
        public async ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
            Add request, CancellationToken cancellationToken)
        {
            // Async by construction is the point: a rule that asks a database is the common case.
            await Task.Yield();
            return [new RequestValidationError("B", "B is suspicious.")];
        }
    }

    private sealed class RejectsWholeRequest : IRequestValidator<Add>
    {
        public ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
            Add request, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RequestValidationError>>(
                [new RequestValidationError(string.Empty, "The request as a whole is wrong.")]);
    }
}
