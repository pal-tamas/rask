namespace Rask.Core.Forms;

/// <summary>What a .NET regular expression is as a browser's <c>pattern</c>, and the facts about it a caller asks.</summary>
/// <param name="Html">The expression as the browser is to read it (the <c>v</c> flag), anchors still in place.</param>
/// <param name="Alternates">Whether it has a <c>|</c> outside every group, so its edges are not one anchor's.</param>
/// <param name="OpensAnchored">Whether it starts with <c>^</c>.</param>
/// <param name="ClosesAnchored">Whether it ends with <c>$</c>.</param>
/// <param name="Anchors">How many <c>^</c> and <c>$</c> it holds.</param>
/// <param name="LooksAround">Whether it holds a lookahead.</param>
internal sealed record ScannedPattern(string Html, bool Alternates, bool OpensAnchored, bool ClosesAnchored, int Anchors, bool LooksAround);
