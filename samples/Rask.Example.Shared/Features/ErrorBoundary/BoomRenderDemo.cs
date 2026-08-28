namespace Rask.Example.Shared.Features;

// The same ErrorBoundary catches synchronous exceptions thrown inside a
// descendant's Render(). Clicking the button flips a flag; the next render of
// the child throws and the fallback replaces it.
public sealed partial class BoomRenderDemo : Component
{
    private bool _throwOnRender;

    protected override Component? Render() =>
        ErrorBoundary
            .Fallback(// Recover for the render-throw demo must ALSO reset _throwOnRender —
                      // otherwise the boundary clears its error, re-walks its cached Children
                      // (still containing the RenderThrower built last frame), and trips
                      // again on the same exception. Two cooperating dirty-marks are needed:
                      //   - recover()         → clears boundary._error, marks boundary dirty
                      //   - StateHasChanged() → marks THIS demo dirty so its Render re-
                      //                         executes with _throwOnRender=false and the
                      //                         boundary receives fresh Children without the
                      //                         RenderThrower.
                      // The handler-throw demo doesn't need this because the underlying
                      // state isn't stale across the trip.
            (ex, recover) => BoundaryFallback(ex, () =>
            {
                // Order matters: dirty-mark this demo FIRST so its re-render calls
                // the ErrorBoundary factory → boundary.SetProps with fresh Children
                // that no longer include the RenderThrower. THEN clear the boundary's
                // error. Calling recover() before that would synchronously re-render
                // the boundary against the stale cached Children (still containing
                // RenderThrower) — the boundary would trip again on the same
                // exception and the recovery would appear to do nothing.
                _throwOnRender = false;
                StateHasChanged();
                recover();
            }))[
            Div.Class("p-3 border rounded bg-white").Id("boom-render-host")[
                P.Class("text-secondary small mb-2")["Healthy. Click below to make my next render throw."],
                Button.Type("button").Class(Ui.BtnWarning).Id("boom-render-trigger").OnClick(() => _throwOnRender = true)[Icon.Name(IconName.Bug).Class("me-2"), "Throw on next render"],
#pragma warning disable RASK014
                // Intentionally bypass the factory: RenderThrower is [SkipFactory] and
                // exists only to demonstrate that a descendant whose Render() throws is
                // caught by the enclosing ErrorBoundary.
                _throwOnRender ? new RenderThrower() : Text.Value(string.Empty)
#pragma warning restore RASK014
            ]
        ];

    private static Component BoundaryFallback(Exception ex, Action recover) =>
        Div.Class($"{Ui.AlertDanger} d-flex align-items-start").Id("boom-fallback")[
            Icon.Name(IconName.ExclamationOctagonFill).Class("me-3 fs-4"),
            Div[
                Strong["Boundary caught: "],
                Code.Class("ms-1")[ex.GetType().Name],
                P.Class("mb-2 mt-1 small")[ex.Message],
                Button.Type("button").Class(Ui.BtnOutlineSecondary).Id("boom-recover").OnClick(recover)[Icon.Name(IconName.ArrowCounterclockwise).Class("me-1"), "Recover"]
            ]
        ];

    // Trivial component whose Render always throws — used to demonstrate render-time
    // boundary capture. SkipFactory tells the source generator not to emit a public
    // factory; we instantiate it directly from inside the boundary's Children.
    [SkipFactory]
    private sealed class RenderThrower : Component
    {
        protected override Component? Render() =>
            throw new InvalidOperationException("kaboom — render-time boundary demo");
    }
}
