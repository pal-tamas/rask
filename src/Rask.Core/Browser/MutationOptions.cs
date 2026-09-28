namespace Rask.Core.Browser;

/// <summary>Tuning for an observation (the <c>MutationObserver</c> options). At least one of
/// <see cref="ChildList" />, <see cref="Attributes" />, or <see cref="CharacterData" /> must be true.</summary>
public sealed record MutationOptions
{
    /// <summary>Observe additions/removals of child nodes (default <c>true</c>).</summary>
    public bool ChildList { get; init; } = true;

    /// <summary>Observe attribute changes.</summary>
    public bool Attributes { get; init; }

    /// <summary>Observe changes to text content (<c>characterData</c>).</summary>
    public bool CharacterData { get; init; }

    /// <summary>Extend observation to the entire subtree, not just the direct target.</summary>
    public bool Subtree { get; init; }

    /// <summary>When set, only these attribute names are observed (implies <see cref="Attributes" />).</summary>
    public string[]? AttributeFilter { get; init; }
}
