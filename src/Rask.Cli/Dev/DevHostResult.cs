using Rask.Hosting.Shared;

namespace Rask.Cli.Dev;

/// <summary>The hostname the app is being served on, and what the child process needs to do it.</summary>
internal sealed record DevHostResult(string Hostname, string Url, IReadOnlyDictionary<string, string> Environment);
