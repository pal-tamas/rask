using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// A live Rask-managed container, as read back from its own labels. <see cref="Port"/> is what the app
/// listens on inside the container — carried as a label so the proxy config can be regenerated for a
/// host running several apps that don't all listen on the same port.
/// </summary>
internal readonly record struct DeployedApp(string Container, string App, string Domain, string Color, int Port);
