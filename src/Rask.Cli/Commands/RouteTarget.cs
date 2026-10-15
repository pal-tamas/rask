using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// Where the shared proxy sends one site: a container and the port it listens on — and the hosts
/// (<see cref="Redirects"/>, a site string, empty when none) it answers with a redirect to the site's primary domain.
/// </summary>
internal readonly record struct RouteTarget(string Container, int Port, string Redirects = "");
