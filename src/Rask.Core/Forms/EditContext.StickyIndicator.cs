namespace Rask.Core.Forms;

public sealed partial class EditContext
{
    // Stamps the sticky deadline and schedules a one-shot dismissal render so the
    // Validation.Indicator gets removed promptly when the sticky tail expires
    // (without this timer the IsValidating(field) flip from true to false would
    // only land on the next unrelated render). The render is requested via
    // <see cref="ValidationStateChanged" /> — LiveRenderContext wires that event
    // to the root component's render handle when the EditContext is first
    // attached to a live render.
    private void ArmStickyDismissal(FieldIdentifier field, FieldState state)
    {
        var sticky = ValidatingStickyMs;
        if (sticky <= 0)
        {
            state.StickyUntilUtc = null;
            return;
        }

        state.StickyUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(sticky);
        state.StickyTimer?.Dispose();
        state.StickyTimer = new Timer(static s =>
        {
#pragma warning disable S8969 // the compiler needs it: unboxing an object? is CS8605 without it
            var (ctx, fid) = ((EditContext, FieldIdentifier))s!;
#pragma warning restore S8969
            if (!ctx._states.TryGetValue(fid, out var inner))
            {
                return;
            }

            // Only clear if we're still in the same sticky window: a fresh
            // PendingCount > 0 in the meantime resets StickyUntilUtc and the
            // new cycle owns the dismissal.
            if (inner.PendingCount > 0)
            {
                return;
            }

            inner.StickyUntilUtc = null;
            ctx.ValidationStateChanged?.Invoke(ctx, EventArgs.Empty);
            // Drive the sticky-dismissal render through the host so the
            // indicator actually leaves the DOM. ValidationStateChanged is a
            // user-facing notification; the render request itself goes via the
            // injected callback that LiveRenderContext wires on attach.
            ctx.RequestRender?.Invoke();
        }, (this, field), sticky, Timeout.Infinite);
    }
}
