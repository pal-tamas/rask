using Rask.Core.HeadAssets;

namespace Rask.Core.Components;

/// <summary>
///     Placement marker for component-declared head dependencies. Place once inside
///     <c>&lt;head&gt;</c>. During serialization this renders a sentinel comment;
///     <see cref="Component.RenderAsLiveRoot()" /> post-processes the final HTML and
///     replaces the sentinel with the concatenated, deduplicated set of
///     <see cref="Component.Head" /> declarations contributed by every component
///     currently in the tree. Components that go away on the next render naturally drop
///     out of head — the registry is rebuilt from scratch each pass.
/// </summary>
public sealed class RaskHeadAssets : Component
{
    protected override Component Render() => new Raw(HeadAssetRegistry.Sentinel);
}
