namespace Rask.Cli;

/// <summary>
/// A declared subcommand of a command, e.g. <c>add</c> under <c>rask db</c>. Recording verbs on the schema
/// (rather than in a private array per command) means dispatch, the unknown-action error, <c>--help</c>,
/// and shell completion all read the same list — including the aliases, which used to be invisible in
/// both help and errors.
/// </summary>
internal sealed record VerbInfo(string Name, string Description, IReadOnlyList<string> Aliases);
