namespace Rask.Core.ScopedAssets;

/// <summary>What <see cref="ScopedAssetRegistry.AssetChanged" /> reports: which component's scoped asset changed, and which kind.</summary>
/// <param name="component">The component whose scoped CSS or JS was re-registered.</param>
/// <param name="kind">Whether it was the stylesheet or the script.</param>
public sealed class ScopedAssetChangedEventArgs(Type component, AssetKind kind) : EventArgs
{
    /// <summary>The component whose scoped CSS or JS was re-registered.</summary>
    public Type Component { get; } = component;

    /// <summary>Whether it was the stylesheet or the script.</summary>
    public AssetKind Kind { get; } = kind;
}
