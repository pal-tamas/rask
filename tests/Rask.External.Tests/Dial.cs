namespace Rask.External.Tests;

/// <summary>
///     A Lit gauge taking the module the convention gives it.
/// </summary>
/// <remarks>
///     In its own file on purpose. The pairing rule is that a <c>.ts</c> is picked up when a
///     <c>.cs</c> of the SAME NAME sits beside it, so a component declared inside another file's
///     source pairs with nothing — which is what happens here if this moves back.
/// </remarks>
public sealed partial class Dial : LitComponent
{
    /// <summary>The needle position, 0..1.</summary>
    public double Value { get; set; }
}
