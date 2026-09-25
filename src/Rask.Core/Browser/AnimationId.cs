namespace Rask.Core.Browser;

/// <summary>
///     A running animation. An <c>Animation</c> object cannot cross interop, so the framework holds it
///     and hands back this handle — the same shape <see cref="MediaStreamId" /> uses for a
///     <c>MediaStream</c>.
/// </summary>
/// <param name="Value">The framework-minted id. <c>0</c> means the animation never started.</param>
public readonly record struct AnimationId(int Value)
{
    /// <summary>Whether this handle refers to an animation that actually started.</summary>
    public bool IsValid => Value > 0;
}
