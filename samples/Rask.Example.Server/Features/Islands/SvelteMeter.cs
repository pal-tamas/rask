namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     A meter rendered by <c>SvelteMeter.svelte</c> — an ordinary Svelte 5 component used as an
///     ordinary Rask component.
/// </summary>
/// <remarks>
///     Carries local state of its own (a nudge count the component owns and C# never sees), which is
///     what makes it the demonstration that an update RECONCILES: C# re-rendering a new
///     <see cref="Value" /> must not reset it.
/// </remarks>
public sealed partial class SvelteMeter : Rask.External.SvelteComponent
{
    /// <summary>The reading, 0..100, owned by C#.</summary>
    public int Value { get; set; }

    /// <summary>The caption beside the reading.</summary>
    public required string Label { get; set; }
}
