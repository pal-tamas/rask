using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// Covers EditContext disposal on form unmount. A field's sticky-dismissal timer (default 200ms
// tail after async validation) is a one-shot Timer that would otherwise fire once after the form
// is torn down — pinning the context + render handle and possibly requesting a stale render. The
// live render now disposes EditContexts that don't survive a root re-render.
public partial class EditContextDisposalTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void FormUnmount_DisposesEditContext()
    {
        var model = new Model { Name = "ada" };
        var ctx = new EditContext(model);
        var show = true;

        var page = RaskTest.Render(() => show
            ? Form<Model>(model, Context: ctx)[Input(() => model.Name)]
            : Div[Text.Value("gone")]);

        Assert.False(ctx.IsDisposed);

        show = false;
        page.Render(); // form unmounted → ctx not re-resolved this frame
        Assert.True(ctx.IsDisposed);
    }

    [Fact]
    public void SurvivingForm_AcrossReRender_IsNotDisposed()
    {
        var model = new Model { Name = "ada" };
        var ctx = new EditContext(model);
        var page = RaskTest.Render(() => Form<Model>(model, Context: ctx)[Input(() => model.Name)]);

        page.Render();

        Assert.False(ctx.IsDisposed);
    }

    [Fact]
    public async Task Dispose_CancelsPendingStickyTimer()
    {
        var model = new Model { Name = "ada" };
        var ctx = new EditContext(model) { ValidatingStickyMs = 100 };
        var renderRequests = 0;
        ctx.RequestRender = () => Interlocked.Increment(ref renderRequests);
        ctx.AddValidator(new NoOpAsyncValidator());

        // Completes immediately but takes the async path (an async validator is registered),
        // so the finally arms the 100ms sticky timer.
        await ctx.ValidateFieldAsync(new FieldIdentifier(model, "Name"));

        ctx.Dispose(); // must dispose the armed timer before it fires
        await Task.Delay(250); // well past the sticky window

        Assert.Equal(0, renderRequests);
        Assert.True(ctx.IsDisposed);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndClearsRequestRender()
    {
        var ctx = new EditContext(new Model());
        ctx.RequestRender = () => { };

        ctx.Dispose();
        Assert.True(ctx.IsDisposed);
        Assert.Null(ctx.RequestRender);

        ctx.Dispose(); // second dispose must not throw
        Assert.True(ctx.IsDisposed);
    }

    [Fact]
    public async Task Dispose_WhileAsyncValidationInFlight_CompletesCleanly()
    {
        // Worst case: a form unmounts (Dispose) while an async field validator is still awaiting,
        // then the validator resumes. The end-to-end handler serialization in the live transports
        // normally prevents this overlap, but the EditContext itself must also be safe — Dispose
        // disposes the per-field CTS, so the resuming validator must not trip an
        // ObjectDisposedException (double CTS dispose) or fire a stale render after RequestRender
        // was nulled.
        var model = new Model { Name = "ada" };
        var ctx = new EditContext(model);
        var renderRequests = 0;
        ctx.RequestRender = () => Interlocked.Increment(ref renderRequests);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ctx.AddValidator(new GatedAsyncValidator(gate));

        // Start validation; it parks inside the gated async validator's await.
        var validation = ctx.ValidateFieldAsync(new FieldIdentifier(model, "Name"));
        await Task.Yield();

        ctx.Dispose();          // form unmounts mid-validation
        gate.SetResult();       // validator resumes after disposal

        var ex = await Record.ExceptionAsync(async () => await validation);
        Assert.Null(ex);
        Assert.True(ctx.IsDisposed);
        Assert.Equal(0, renderRequests); // RequestRender was nulled on Dispose — no stale render
    }

    private sealed class Model
    {
        public string Name { get; set; } = "";
    }

    private sealed class NoOpAsyncValidator : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken) => default;

        public ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
            CancellationToken cancellationToken) => default;
    }

    // Async validator that parks on a test-controlled gate, so the test can dispose the EditContext
    // while a field validation is mid-await and then release it.
    private sealed class GatedAsyncValidator(TaskCompletionSource gate) : IAsyncFieldValidator
    {
        public ValueTask ValidateAsync(EditContext context, CancellationToken cancellationToken) => default;

        public async ValueTask ValidateFieldAsync(EditContext context, FieldIdentifier field,
            CancellationToken cancellationToken) =>
            await gate.Task.ConfigureAwait(false);
    }
}
