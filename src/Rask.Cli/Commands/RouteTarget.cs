using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>Where the shared proxy sends one domain: a container and the port it listens on.</summary>
internal readonly record struct RouteTarget(string Container, int Port);
