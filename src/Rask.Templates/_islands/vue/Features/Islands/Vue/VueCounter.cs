namespace Company.RaskServer.Features.Islands;

/// <summary>
///     A counter rendered by <c>VueCounter.vue</c> — an ordinary Vue component used as an
///     ordinary Rask component.
/// </summary>
/// <remarks>
///     The base class IS the declaration: it names the runtime in the one place that cannot disagree
///     with what actually mounts. Props are declared here and generated into
///     <c>@rask/VueCounter.props</c>, so renaming one stops the front-end file compiling — the contract is
///     checked in both directions rather than maintained by discipline.
/// </remarks>
public sealed partial class VueCounter : Rask.External.VueComponent
{
    /// <summary>The step C# hands it, which the component adds on each press.</summary>
    public int Step { get; set; } = 1;

    /// <summary>The caption above the counter.</summary>
    public required string Caption { get; set; }

    /// <summary>Runs with the component's running total whenever it changes.</summary>
    public Callback<int>? OnTotalChanged { get; set; }
}
