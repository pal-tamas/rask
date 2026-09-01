namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     A status badge rendered by <c>LitBadge.ts</c> — a Lit element used as an ordinary Rask component.
/// </summary>
/// <remarks>
///     The cheapest runtime of the set: a Lit component IS a custom element, so mounting is
///     <c>createElement</c> plus property assignment and there is no reconciler to drive. Its
///     <c>.ts</c> is paired by the same filename rule, which is what naming the runtime in the base
///     class bought — nothing about a <c>.ts</c> extension distinguishes a Lit component from any
///     other TypeScript in the project.
/// </remarks>
public sealed partial class LitBadge : Rask.External.LitComponent
{
    /// <summary>The label shown in the badge.</summary>
    public required string Label { get; set; }

    /// <summary>How many times C# has re-rendered it, so a prop change is visible.</summary>
    public int Revision { get; set; }
}
