namespace Rask.Cli.Commands;

/// <summary>How a <see cref="ContainerStop"/> ended: stopped (exited, or killed at the end of its budget), not there, or unknown.</summary>
internal enum StopResult
{
    Stopped,
    Absent,
    Unknown,
}
