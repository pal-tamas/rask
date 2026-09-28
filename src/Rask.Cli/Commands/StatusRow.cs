using Rask.Cli.Scaffolding;
using Spectre.Console;

namespace Rask.Cli.Commands;

/// <summary>One row of <c>rask deploy status</c>, as the host reported it.</summary>
internal readonly record struct StatusRow(string Container, string App, string Domain, string Color, string Status, string Ports);
