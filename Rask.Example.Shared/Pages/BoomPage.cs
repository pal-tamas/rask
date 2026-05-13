using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("boom")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BoomPage : Component
{
    private bool _throwOnRender;

    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Error boundary",
                "ErrorBoundary catches exceptions thrown by descendants — render-time, sync lifecycle, async lifecycle, and event handlers — and renders a fallback in their place. The fallback receives a Recover() callback so the boundary can be reset from a button click."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Handler throw — boundary catches and renders fallback"]),
            CodeSample(
                """
                ErrorBoundary(
                    Fallback: (ex, recover) => Div(Children: [
                        Strong(Children: ["Caught: "]), ex.Message,
                        Button(OnClick: recover, Children: ["Reset"])
                    ]),
                    Children: [
                        Button(OnClick: () => throw new InvalidOperationException("kaboom"),
                               Children: ["Throw"])
                    ])
                """,
                Result: ErrorBoundary(
                    Fallback: BoundaryFallback,
                    Children:
                    [
                        Div(Class: "p-3 border rounded bg-white", Id: "boom-handler-host", Children:
                        [
                            P(Class: "text-secondary small mb-2",
                                Children: ["Healthy subtree — click to throw."]),
                            Button(
                                Class: "btn btn-danger",
                                Id: "boom-throw",
                                OnClick: ThrowFromHandler,
                                Children: [I(Class: "bi bi-exclamation-triangle me-2"), "Throw a handler exception"])
                        ])
                    ])),
            H2(Class: "h4 mt-5 mb-3", Children: ["Render-time throw"]),
            CodeSample(
                """
                ErrorBoundary(
                    Fallback: (ex, recover) => ...,
                    Children: [_throwOnRender ? new RenderThrower() : Div(...)])
                """,
                Notes:
                "The same boundary catches synchronous exceptions thrown inside a descendant's Render(). Click below to flip a flag; the next render of the child throws and the fallback replaces it.",
                Result: ErrorBoundary(
                    // Recover for the render-throw demo must ALSO reset _throwOnRender —
                    // otherwise the boundary clears its error, re-walks its cached Children
                    // (still containing the RenderThrower built last frame), and trips
                    // again on the same exception. Two cooperating dirty-marks are needed:
                    //   - recover()         → clears boundary._error, marks boundary dirty
                    //   - StateHasChanged() → marks THIS BoomPage dirty so its Render re-
                    //                         executes with _throwOnRender=false and the
                    //                         boundary receives fresh Children without the
                    //                         RenderThrower.
                    // The handler-throw demo above doesn't need this because the underlying
                    // state isn't stale across the trip.
                    Fallback: (ex, recover) => BoundaryFallback(ex, () =>
                    {
                        // Order matters: dirty-mark BoomPage FIRST so its re-render calls
                        // Tags.ErrorBoundary → boundary.SetProps with fresh Children that
                        // no longer include the RenderThrower. THEN clear the boundary's
                        // error. Calling recover() before that would synchronously re-render
                        // the boundary against the stale cached Children (still containing
                        // RenderThrower) — the boundary would trip again on the same
                        // exception and the recovery would appear to do nothing.
                        _throwOnRender = false;
                        StateHasChanged();
                        recover();
                    }),
                    Children:
                    [
                        Div(Class: "p-3 border rounded bg-white", Id: "boom-render-host", Children:
                        [
                            P(Class: "text-secondary small mb-2",
                                Children: ["Healthy. Click below to make my next render throw."]),
                            Button(
                                Class: "btn btn-warning",
                                Id: "boom-render-trigger",
                                OnClick: () => _throwOnRender = true,
                                Children: [I(Class: "bi bi-bug me-2"), "Throw on next render"]),
#pragma warning disable RASK014
                            // Intentionally bypass the factory: RenderThrower is [SkipFactory] and
                            // exists only to demonstrate that a descendant whose Render() throws is
                            // caught by the enclosing ErrorBoundary.
                            _throwOnRender ? (Child)new RenderThrower() : Text(string.Empty)
#pragma warning restore RASK014
                        ])
                    ])),
            H2(Class: "h4 mt-5 mb-3", Children: ["Nested boundaries — inner catches first"]),
            CodeSample(
                """
                ErrorBoundary(  // outer
                    Fallback: outerFallback,
                    Children: [
                        Div(Children: [
                            P(Children: ["Outer healthy region — survives inner throws."]),
                            ErrorBoundary(  // inner
                                Fallback: innerFallback,
                                Children: [
                                    Button(OnClick: () => throw new InvalidOperationException("inner kaboom"),
                                           Children: ["Throw inside inner boundary"])
                                ])
                        ])
                    ])
                """,
                Notes:
                "The inner boundary catches first — the outer healthy region (and its sibling paragraph) stays mounted. If the inner fallback itself throws, the outer boundary catches the escalation.",
                Result: ErrorBoundary(
                    Fallback: (ex, _) => OuterFallback(ex),
                    Children:
                    [
                        Div(Class: "p-3 border rounded bg-white", Id: "boom-nested-host", Children:
                        [
                            P(Class: "mb-2 small text-secondary",
                                Id: "boom-nested-outer-healthy",
                                Children: ["Outer healthy region — stays mounted while the inner boundary trips."]),
                            ErrorBoundary(
                                Fallback: (ex, recover) => InnerFallback(ex, recover),
                                Children:
                                [
                                    Div(Class: "p-3 border rounded bg-light", Children:
                                    [
                                        P(Class: "small text-secondary mb-2",
                                            Children: ["Inner boundary subtree."]),
                                        Button(
                                            Class: "btn btn-danger btn-sm",
                                            Id: "boom-nested-throw",
                                            OnClick: ThrowFromInnerHandler,
                                            Children: [I(Class: "bi bi-exclamation-triangle me-2"),
                                                       "Throw inside inner boundary"])
                                    ])
                                ])
                        ])
                    ]))
        );

    private static Child InnerFallback(Exception ex, Action recover) =>
        Div(Class: "alert alert-warning d-flex align-items-start",
            Id: "boom-nested-inner-fallback", Children:
        [
            I(Class: "bi bi-shield-exclamation me-3 fs-4"),
            Div(Children:
            [
                Strong(Children: ["Inner boundary caught: "]),
                Code(Class: "ms-1", Children: [ex.GetType().Name]),
                P(Class: "mb-2 mt-1 small", Children: [ex.Message]),
                Button(
                    Class: "btn btn-sm btn-outline-secondary",
                    Id: "boom-nested-inner-recover",
                    OnClick: recover,
                    Children: [I(Class: "bi bi-arrow-counterclockwise me-1"), "Recover inner"])
            ])
        ]);

    private static Child OuterFallback(Exception ex) =>
        Div(Class: "alert alert-danger", Id: "boom-nested-outer-fallback", Children:
        [
            Strong(Children: ["Outer boundary caught: "]), ex.Message
        ]);

    private static void ThrowFromInnerHandler() =>
        throw new InvalidOperationException("kaboom — inner boundary demo");

    private static Child BoundaryFallback(Exception ex, Action recover) =>
        Div(Class: "alert alert-danger d-flex align-items-start", Id: "boom-fallback", Children:
        [
            I(Class: "bi bi-exclamation-octagon-fill me-3 fs-4"),
            Div(Children:
            [
                Strong(Children: ["Boundary caught: "]),
                Code(Class: "ms-1", Children: [ex.GetType().Name]),
                P(Class: "mb-2 mt-1 small", Children: [ex.Message]),
                Button(
                    Class: "btn btn-sm btn-outline-secondary",
                    Id: "boom-recover",
                    OnClick: recover,
                    Children: [I(Class: "bi bi-arrow-counterclockwise me-1"), "Recover"])
            ])
        ]);

    private static void ThrowFromHandler() =>
        throw new InvalidOperationException("kaboom — handler boundary demo");

    // Trivial component whose Render always throws — used to demonstrate render-time
    // boundary capture. SkipFactory tells the source generator not to emit a public
    // factory; we instantiate it directly from inside the boundary's Children.
    [SkipFactory]
    private sealed class RenderThrower : Component
    {
        protected override Component Render() =>
            throw new InvalidOperationException("kaboom — render-time boundary demo");
    }
}
