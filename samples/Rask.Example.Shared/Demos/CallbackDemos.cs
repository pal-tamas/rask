using System.Linq;
using Rask.Core;

namespace Rask.Example.Shared.Demos;

// A reusable child component that knows nothing about its parent's state. It renders clickable
// stars and emits the chosen rating up through a Callback<int>. Because the callback captures
// the parent as its receiver, invoking it re-renders the parent — without the child holding a
// reference to it or the parent threading StateHasChanged through by hand.
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Callback<int> OnRate { get; set; }

    protected override RenderResult Render() =>
        Div(Class: "d-inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => (Child)Button(
                Class: "btn btn-link p-0 fs-3 lh-1 text-decoration-none",
                Style: i <= Value ? "color:#ffc107" : "color:#ced4da",
                OnClick: async () => await OnRate.InvokeAsync(i),
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
            // The lambda captures `this`, so the callback's receiver is this demo — clicking a
            // star in the child re-renders the line below.
            RatingStars(Value: _rating, OnRate: Callback.Create<int>(n => _rating = n)),
            P(Class: "mt-2 mb-0 small text-secondary")[
                _rating == 0 ? "Click a star to rate." : $"You rated: {_rating}/5"
            ]
        ];
}
