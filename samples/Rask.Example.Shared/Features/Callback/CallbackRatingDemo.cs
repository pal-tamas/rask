namespace Rask.Example.Shared.Features;

public sealed partial class CallbackRatingDemo : Component
{
    private int _rating;

    protected override Component? Render() =>
        Div.Id("callback-rating")[
            // The lambda captures `this`, so it owns this demo — the framework wraps it so clicking
            // a star in the child re-renders the line below, with no extra ceremony.
            RatingStars.Value(_rating).OnRate(n => _rating = n),
            P.Class("mt-2 mb-0 text-sm text-slate-500 dark:text-slate-400")[
                _rating == 0 ? "Click a star to rate." : $"You rated: {_rating}/5"
            ]
        ];
}
