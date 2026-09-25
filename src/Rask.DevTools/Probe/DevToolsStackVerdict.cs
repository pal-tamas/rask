namespace Rask.DevTools.Probe;

/// <summary>What a stack says about who is to blame: the framework, or the app.</summary>
/// <param name="LikelyFrameworkBug">The innermost frame that is neither the runtime's nor the base library's is Rask's.</param>
/// <param name="Frames">
///     The frames a report may carry, innermost first: Rask's, by name only, and every run of anything else collapsed to
///     <c>[app code]</c>. No file paths, no arguments.
/// </param>
internal sealed record DevToolsStackVerdict(bool LikelyFrameworkBug, IReadOnlyList<string> Frames)
{
    internal static readonly DevToolsStackVerdict None = new(false, []);
}
