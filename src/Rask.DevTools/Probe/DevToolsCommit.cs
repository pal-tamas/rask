namespace Rask.DevTools.Probe;

/// <summary>One render the page committed: every component whose <c>Render()</c> ran in it, in the order they ran.</summary>
/// <param name="Sequence">Monotonic per feed, counted with the wire events, so the two tabs' numbers never collide.</param>
/// <param name="Timestamp">When it committed, as a <see cref="System.Diagnostics.Stopwatch" /> timestamp.</param>
/// <param name="Walked">How many components the walk passed through, rendered or served from their cache.</param>
/// <param name="Renders">The components that actually rendered.</param>
internal sealed record DevToolsCommit(long Sequence, long Timestamp, int Walked, DevToolsRender[] Renders);
