namespace Rask.Cli;

/// <summary>The outcome of a captured child process.</summary>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
