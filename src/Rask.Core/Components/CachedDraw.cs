namespace Rask.Core.Components;

/// <summary>Where a <see cref="Cached" /> fragment stands with its key.</summary>
internal enum CachedDraw : byte
{
    /// <summary>Its lookup has not landed: nothing is drawn, and what it wraps is held back.</summary>
    Undecided,

    /// <summary>Found: the stored markup is drawn, and what it wraps is never mounted.</summary>
    Stored,

    /// <summary>Not found: what it wraps is drawn live, and its markup stored once settled.</summary>
    Live,

    /// <summary>No safe key: what it wraps is drawn live and never stored.</summary>
    Uncached
}
