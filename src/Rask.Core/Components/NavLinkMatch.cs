namespace Rask.Core.Components;

/// <summary>
///     How a <see cref="NavLink" /> decides it is pointing at the current page.
/// </summary>
public enum NavLinkMatch
{
    /// <summary>Active only when the current path equals the link's, exactly.</summary>
    Exact,

    /// <summary>Active when the current path starts with the link's — what keeps a section's parent link highlighted while one of its child pages is open.</summary>
    Prefix
}
