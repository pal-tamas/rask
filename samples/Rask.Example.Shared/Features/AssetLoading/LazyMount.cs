namespace Rask.Example.Shared.Features;

/// <summary>
///     Lazy mount demo: a Show/Hide toggle that mounts/unmounts <see cref="LazyChild" />.
///     When mounted, the framework emits LazyChild's <c>&lt;link&gt;</c> into <c>&lt;head&gt;</c>
///     (browser fetches the CSS for the first time). When unmounted, the morph removes
///     the tag — but the browser keeps the bytes cached, so re-mounting is a cache hit.
/// </summary>
public sealed partial class LazyMount : Component
{
    private static readonly Component Empty = Div;
    private bool _shown;

    protected override Component? Render() =>
        Div[
            BsButton.Color(BsColor.Secondary).Outline(true).Class("mb-3").OnClick(() => _shown = !_shown)[
                _shown ? "Hide LazyChild" : "Show LazyChild"
            ],
            _shown ? LazyChild : Empty
        ];
}
