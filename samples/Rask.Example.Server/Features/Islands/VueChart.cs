namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     A bar chart rendered by <c>VueChart.vue</c> — an ordinary Vue single-file component used as an
///     ordinary Rask component.
/// </summary>
/// <remarks>
///     There is no attribute and no registration. Deriving from <see cref="Rask.External.VueComponent" />
///     is the declaration, and the <c>.vue</c> beside this file is paired by name the way scoped CSS and
///     scoped JS already are.
/// </remarks>
public sealed partial class VueChart : Rask.External.VueComponent
{
    /// <summary>The bars to plot.</summary>
    public required IReadOnlyList<ChartBar> Series { get; set; }

    /// <summary>Heading shown above the plot.</summary>
    public string? Heading { get; set; }

    /// <summary>Runs when a bar is clicked, with that bar's value — straight back into C#.</summary>
    public Action<int>? OnBarClick { get; set; }
}

/// <summary>One plotted bar. A record composed of wire-encodable types, so it crosses as JSON.</summary>
/// <param name="Label">The bar's caption.</param>
/// <param name="Value">The bar's height, 0..100.</param>
public sealed record ChartBar(string Label, int Value);
