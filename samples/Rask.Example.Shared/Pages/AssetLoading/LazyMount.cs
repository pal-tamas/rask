namespace Rask.Example.Shared.Pages.AssetLoading;

/// <summary>
///     Lazy mount demo: a Show/Hide toggle that mounts/unmounts <see cref="LazyChild" />.
///     When mounted, the framework emits LazyChild's <c>&lt;link&gt;</c> into <c>&lt;head&gt;</c>
///     (browser fetches the CSS for the first time). When unmounted, the morph removes
///     the tag — but the browser keeps the bytes cached, so re-mounting is a cache hit.
/// </summary>
public sealed class LazyMount : Component
{
    private bool _shown;

    protected override RenderResult Render() =>
        Div()[
            Button(
                Class: "btn btn-outline-secondary mb-3",
                OnClick: () => _shown = !_shown)[
                _shown ? "Hide LazyChild" : "Show LazyChild"
            ],
            _shown ? LazyChild() : Empty
        ];

    private static readonly Component Empty = Div();
}
