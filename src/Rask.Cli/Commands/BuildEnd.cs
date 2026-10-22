namespace Rask.Cli.Commands;

/// <summary>How an image build ended: <see cref="ImageBuild"/> stops one that runs too long or goes quiet.</summary>
internal enum BuildEnd
{
    Built,
    Failed,

    /// <summary>It ran past <see cref="DeployDeadlines.Build"/>.</summary>
    TimedOut,

    /// <summary>It printed nothing for <see cref="DeployDeadlines.BuildIdle"/> — a stalled download, or a dead connection.</summary>
    WentQuiet,
}
