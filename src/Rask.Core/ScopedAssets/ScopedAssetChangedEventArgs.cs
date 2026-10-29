namespace Rask.Core.ScopedAssets;

/// <summary>What <see cref="ScopedAssetRegistry.AssetChanged" /> reports: which component's scoped asset changed, and which kind.</summary>
/// <param name="component">The component whose scoped CSS or JS was registered, re-registered or dropped.</param>
/// <param name="kind">Whether it was the stylesheet or the script.</param>
/// <param name="replaced">Whether an asset the component already had was replaced or dropped.</param>
public sealed class ScopedAssetChangedEventArgs(Type component, AssetKind kind, bool replaced) : EventArgs
{
    /// <summary>The component whose scoped CSS or JS was registered, re-registered or dropped.</summary>
    public Type Component { get; } = component;

    /// <summary>Whether it was the stylesheet or the script.</summary>
    public AssetKind Kind { get; } = kind;

    /// <summary>
    ///     Whether an asset the component already had was replaced or dropped. False for its first registration — the
    ///     module initializer of an assembly loading, at startup or lazily mid-run — which no page can be showing yet,
    ///     since nothing of that assembly has rendered: nothing on screen needs repainting for it.
    /// </summary>
    public bool Replaced { get; } = replaced;
}
