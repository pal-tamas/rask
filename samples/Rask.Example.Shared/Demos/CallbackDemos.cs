using System.Linq;
using Rask.Core;

namespace Rask.Example.Shared.Demos;

// A reusable child component that knows nothing about its parent's state. It renders clickable
// stars and emits the chosen rating up through a plain Action<int> prop. The child wraps the
// callback in its own click handler (so the DOM event only dirties the child) and invokes it off
// that path — yet the parent still re-renders, because the framework auto-wraps the delegate to
// re-render its owner. No Callback type, no StateHasChanged threaded through by hand.
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }

    protected override RenderResult Render() =>
        Div(Class: "d-inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => (Child)Button(
                Class: "btn btn-link p-0 fs-3 lh-1 text-decoration-none",
                Style: i <= Value ? "color:#ffc107" : "color:#ced4da",
                OnClick: () => OnRate?.Invoke(i),
                Key: i)[
                i <= Value ? "★" : "☆"
            ])
        ];
}

public sealed class CallbackRatingDemo : Component
{
    private int _rating;

    protected override RenderResult Render() =>
        Div()[
            // The lambda captures `this`, so it owns this demo — the framework wraps it so clicking
            // a star in the child re-renders the line below, with no extra ceremony.
            RatingStars(Value: _rating, OnRate: n => _rating = n),
            P(Class: "mt-2 mb-0 small text-secondary")[
                _rating == 0 ? "Click a star to rate." : $"You rated: {_rating}/5"
            ]
        ];
}
