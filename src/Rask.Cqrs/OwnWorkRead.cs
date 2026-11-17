namespace Rask.Background;

/// <summary>What the server answers <see cref="ReadOwnWork" /> with.</summary>
/// <param name="Every">How often the rows change: the server board's <see cref="IWorkBoard.Every" />.</param>
/// <param name="Work">The rows, as the server board read them.</param>
internal sealed record OwnWorkRead(TimeSpan Every, IReadOnlyList<WorkReported> Work);
