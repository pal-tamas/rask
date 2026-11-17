namespace Rask.Background;

/// <summary>Where a piece of background work stands, as <see cref="WorkReported" /> says it.</summary>
public enum WorkState
{
    /// <summary>It waits to run, runs, or waits to be tried again.</summary>
    Running,

    /// <summary>It ran to the end.</summary>
    Done,

    /// <summary>It failed and will not be tried again.</summary>
    Failed,

    /// <summary>It was stopped before it finished — asked to, or past a limit.</summary>
    Stopped,
}
