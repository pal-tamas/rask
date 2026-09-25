namespace Rask.DevTools.Probe;

/// <summary>A context value a component read while it rendered, and where it came from.</summary>
/// <param name="Type">The type it asked for.</param>
/// <param name="Name">The name it asked by, when it gave one.</param>
/// <param name="Found">Whether a provider answered. A read nothing answered is still a read: <c>Has</c> asked and got no.</param>
/// <param name="ProviderId">The tree id of the component whose markup provided it, or null.</param>
/// <param name="ProviderType">That component's name, or null.</param>
internal sealed record DevToolsReadContext(string Type, string? Name, bool Found, long? ProviderId, string? ProviderType);
