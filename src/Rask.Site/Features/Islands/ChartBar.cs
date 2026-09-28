namespace Rask.Site.Features.Islands;

/// <summary>One plotted bar. A record composed of wire-encodable types, so it crosses as JSON.</summary>
/// <param name="Label">The bar's caption.</param>
/// <param name="Value">The bar's height, 0..100.</param>
public sealed record ChartBar(string Label, int Value);
