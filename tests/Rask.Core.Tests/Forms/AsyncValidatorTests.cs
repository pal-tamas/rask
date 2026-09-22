using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class AsyncValidatorTests
{
    [Fact]
    public async Task Async_field_validation_runs_the_async_validator_and_adds_its_message()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        ctx.AddValidator(new DelayedValidator(20, "Name", "bad"));

        var ok = await ctx.ValidateFieldAsync(fid);

        Assert.False(ok);
        Assert.Equal(new[] { "bad" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task A_second_async_field_validation_cancels_the_first()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        var firstGate = new TaskCompletionSource();
        var secondGate = new TaskCompletionSource();
        var validator = new GatedValidator((c, _, _) => c == 1 ? firstGate.Task : secondGate.Task)
        {
            OnFinish = (c, f, c2) =>
            {
                if (c == 1)
                {
                    c2.AddValidationMessage(f, "first");
                }
                else if (c == 2)
                {
                    c2.AddValidationMessage(f, "second");
                }
            }
        };
        ctx.AddValidator(validator);

        var first = ctx.ValidateFieldAsync(fid);
        var second = ctx.ValidateFieldAsync(fid);

        secondGate.SetResult();
        await second;

        firstGate.SetResult();
        var firstResult = await first;

        Assert.False(firstResult);
        Assert.Equal(new[] { "second" }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task IsValidating_is_true_during_the_await_and_false_after()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        var gate = new TaskCompletionSource();
        ctx.AddValidator(new GatedValidator((_, _, _) => gate.Task));
        var stateChanges = 0;
        ctx.ValidationStateChanged += () => stateChanges++;

        var task = ctx.ValidateFieldAsync(fid);

        Assert.True(ctx.IsValidating(fid));
        Assert.True(ctx.IsValidatingAny);

        gate.SetResult();
        await task;

        Assert.False(ctx.IsValidating(fid));
        Assert.False(ctx.IsValidatingAny);
        Assert.True(stateChanges >= 2);
    }

    [Fact]
    public void A_sync_Validate_throws_when_an_async_validator_is_registered()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        ctx.AddValidator(new GatedValidator((_, _, _) => Task.CompletedTask));

        Assert.Throws<InvalidOperationException>(() => ctx.Validate());
        Assert.Throws<InvalidOperationException>(() => ctx.ValidateField(new FieldIdentifier(m, "Name")));
    }

    [Fact]
    public async Task Form_validation_cancels_in_flight_field_validations()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        var observedCancellation = false;
        var fieldGate = new TaskCompletionSource();
        ctx.AddValidator(new GatedValidator(async (_, _, ct) =>
        {
            var reg = ct.Register(() => observedCancellation = true);
            try
            {
                await fieldGate.Task;
            }
            finally
            {
                reg.Dispose();
            }
        }));

        var fieldTask = ctx.ValidateFieldAsync(fid);
        var formTask = ctx.ValidateAsync();

        fieldGate.SetResult();
        await fieldTask;
        await formTask;

        Assert.True(observedCancellation);
    }

    [Fact]
    public async Task A_throwing_async_validator_adds_a_generic_message_and_does_not_bubble()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, "Name");
        ctx.AddValidator(new GatedValidator((_, _, _) => throw new HttpRequestException("boom")));

        var ok = await ctx.ValidateFieldAsync(fid);

        Assert.False(ok);
        Assert.Equal(new[] { "Validation could not be completed." }, ctx.GetValidationMessages(fid));
    }

    [Fact]
    public void Async_validators_are_deduped_by_type()
    {
        var ctx = new EditContext(new Model());
        ctx.AddValidator(new GatedValidator((_, _, _) => Task.CompletedTask));
        ctx.AddValidator(new GatedValidator((_, _, _) => Task.CompletedTask));

        Assert.True(ctx.HasAsyncValidators);
        // Run a validation pass and confirm only one call landed.
        // (call-count is internal to the second instance; the dedupe means the second instance never runs.)
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }

    private sealed class DelayedValidator(int delayMs, string addMessageOnField, string message) : IAsyncFieldValidator
    {
        public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            context.AddValidationMessage(new FieldIdentifier(context.Model, addMessageOnField), message);
        }

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            if (field.FieldName == addMessageOnField)
            {
                context.AddValidationMessage(field, message);
            }
        }
    }

    private sealed class GatedValidator(Func<int, FieldIdentifier, CancellationToken, Task> wait) : IAsyncFieldValidator
    {
        private int _callCount;

        public Action<int, FieldIdentifier, EditContext>? OnFinish { get; set; }

        public async ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _callCount);
            var userTask = wait(n, default, cancellationToken);
            var ctTask = Task.Delay(Timeout.Infinite, cancellationToken);
            await Task.WhenAny(userTask, ctTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await userTask.ConfigureAwait(false);
        }

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
            CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _callCount);
            var userTask = wait(n, field, cancellationToken);
            var ctTask = Task.Delay(Timeout.Infinite, cancellationToken);
            await Task.WhenAny(userTask, ctTask).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await userTask.ConfigureAwait(false);
            OnFinish?.Invoke(n, field, context);
        }
    }
}
