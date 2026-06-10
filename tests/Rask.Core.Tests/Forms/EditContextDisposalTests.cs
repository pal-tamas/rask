using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// Covers EditContext disposal on form unmount. A field's sticky-dismissal timer (default 200ms
// tail after async validation) is a one-shot Timer that would otherwise fire once after the form
// is torn down — pinning the context + render handle and possibly requesting a stale render. The
// live render now disposes EditContexts that don't survive a root re-render.
public class EditContextDisposalTests
{
    [Fact]
    public void FormUnmount_DisposesEditContext()
    {
        var model = new Model { Name = "ada" };
        var ctx = new EditContext(model);
        var show = true;

        var view = new StubComponent(() => show
            ? Form<Model>(model, Context: ctx)[Input(() => model.Name)]
            : Div()[Text("gone")]);

        view.RenderAsLiveRoot();
        Assert.False(ctx.IsDisposed);

        show = false;
        view.RenderAsLiveRoot(); // form unmounted → ctx not re-resolved this frame
        Assert.True(ctx.IsDisposed);
    }

    [Fact]
    public void SurvivingForm_AcrossReRender_IsNotDisposed()
    {
        var model = new Model { Name = "ada" };
        var ctx = new EditContext(model);
        var view = new StubComponent(() => Form<Model>(model, Context: ctx)[Input(() => model.Name)]);

        view.RenderAsLiveRoot();
        view.RenderAsLiveRoot();

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
}
