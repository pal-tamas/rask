using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class DelegateValidatorTests
{
    [Fact]
    public async Task A_sync_field_validator_runs_on_ValidateField_and_appends_messages()
    {
        var m = new Model { Name = "ab" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(v => v.Length < 3 ? new[] { "too short" } : Array.Empty<string>()));

        Assert.False(await ctx.ValidateField(fid, TestContext.Current.CancellationToken));
        Assert.Contains("too short", ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task Registering_a_null_field_validator_clears_the_prior_registration()
    {
        var m = new Model { Name = "ab" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(_ => new[] { "boom" }));
        await ctx.ValidateField(fid, TestContext.Current.CancellationToken);
        Assert.NotEmpty(ctx.GetValidationMessages(fid));

        // Drop the registration; re-validate; messages should clear and stay clear.
        ctx.RegisterFieldValidator(fid, null);
        Assert.True(await ctx.ValidateField(fid, TestContext.Current.CancellationToken));
        Assert.Empty(ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task An_async_field_validator_runs_on_ValidateFieldAsync()
    {
        var m = new Model { Name = "x" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.RegisterFieldValidator(fid,
            (Func<string, ValueTask<IEnumerable<string>>>)(async v =>
            {
                await Task.Delay(10, Current.Cancellation).ConfigureAwait(false);
                return v == "x" ? new[] { "bad" } : Array.Empty<string>();
            }));

        Assert.False(await ctx.ValidateField(fid, TestContext.Current.CancellationToken));
        Assert.Contains("bad", ctx.GetValidationMessages(fid));
    }

    [Fact]
    public async Task The_latest_async_field_validation_wins_and_cancels_the_earlier_one()
    {
        var m = new Model { Name = "x" };
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        var firstStarted = new TaskCompletionSource();
        var firstObserved = new TaskCompletionSource<bool>();

        ctx.RegisterFieldValidator(fid,
            (Func<string, ValueTask<IEnumerable<string>>>)(async v =>
            {
                firstStarted.TrySetResult();
                try
                {
                    await Task.Delay(2000, Current.Cancellation).ConfigureAwait(false);
                    firstObserved.TrySetResult(false);
                    return new[] { "first" };
                }
                catch (OperationCanceledException)
                {
                    firstObserved.TrySetResult(true);
                    throw;
                }
            }));

        var firstTask = ctx.ValidateField(fid, TestContext.Current.CancellationToken).AsTask();
        await firstStarted.Task;

        // Re-register with a quick second delegate and validate again. The CTS-based latest-
        // wins path in EditContext.ValidateFieldAsync cancels the first run.
        ctx.RegisterFieldValidator(fid,
            (Func<string, ValueTask<IEnumerable<string>>>)(async _ =>
            {
                await Task.Yield();
                return new[] { "second" };
            }));

        var secondTask = ctx.ValidateField(fid, TestContext.Current.CancellationToken).AsTask();
        await secondTask;

        // First should have observed cancellation.
        Assert.True(await firstObserved.Task);
        // Final messages reflect the second run only.
        var msgs = ctx.GetValidationMessages(fid);
        Assert.Contains("second", msgs);
        Assert.DoesNotContain("first", msgs);
    }

    [Fact]
    public async Task A_sync_form_validator_runs_on_Validate_and_its_messages_attach_to_the_form_field()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);

        ctx.RegisterFormValidator(
            (Func<Model, IEnumerable<string>>)(model =>
                string.IsNullOrEmpty(model.Name) ? new[] { "form bad" } : Array.Empty<string>()));

        Assert.False(await ctx.Validate(TestContext.Current.CancellationToken));
        var formField = new FieldIdentifier(m, "");
        Assert.Contains("form bad", ctx.GetValidationMessages(formField));
    }

    [Fact]
    public async Task An_async_form_validator_runs_on_ValidateAsync()
    {
        var m = new Model { Name = "" };
        var ctx = new EditContext(m);

        ctx.RegisterFormValidator(
            (Func<Model, ValueTask<IEnumerable<string>>>)(async model =>
            {
                await Task.Delay(10, Current.Cancellation).ConfigureAwait(false);
                return string.IsNullOrEmpty(model.Name) ? new[] { "async form bad" } : Array.Empty<string>();
            }));

        Assert.False(await ctx.Validate(TestContext.Current.CancellationToken));
        var formField = new FieldIdentifier(m, "");
        Assert.Contains("async form bad", ctx.GetValidationMessages(formField));
    }

    [Fact]
    public async Task A_throwing_delegate_is_swallowed_into_a_generic_message()
    {
        var m = new Model();
        var ctx = new EditContext(m);
        var fid = new FieldIdentifier(m, nameof(Model.Name));

        ctx.RegisterFieldValidator(fid,
            (Func<string, IEnumerable<string>>)(_ => throw new InvalidOperationException("boom")));

        await ctx.ValidateField(fid, TestContext.Current.CancellationToken);

        Assert.Contains(ctx.GetValidationMessages(fid),
            msg => msg.Contains("could not be completed"));
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }
}
