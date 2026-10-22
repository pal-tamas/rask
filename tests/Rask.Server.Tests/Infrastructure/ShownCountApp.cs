using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>The number <see cref="ShownCountApp" /> draws, set by the test from outside the page.</summary>
public sealed class ShownCount
{
    public int Value { get; set; }
}

// Draws a number a test changes from outside, so a page repainted while no connection is attached owes that
// repaint to the next one.
public sealed partial class ShownCountApp(ShownCount count) : Component
{
    protected override Component? HeadAssets => Markup.Title["shown"];

    protected override string? HtmlLang => null;

    protected override Component? Render() => Markup.H1[$"shown {count.Value}"];
}
