namespace Rask.Cli.Commands;

/// <summary>
/// The container chosen to serve an app, and whether nothing on the box named it, so it is the newest by default.
/// </summary>
internal readonly record struct ServingChoice(DeployedApp Container, bool Guessed);
