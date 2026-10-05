namespace Rask.Cli;

/// <summary>
/// A declared flag or option: its names, whether it takes a value (and a hint for it), a one-line
/// description, an optional <see cref="Group"/> label so help can bucket, say, <c>deploy</c>'s
/// host-setup flags apart from the common ones, and an optional closed set of <see cref="Choices"/>.
/// This is the single source of truth that both parses arguments and documents them — <c>--help</c> and
/// shell completion render straight from this list, so they can never drift.
/// </summary>
internal sealed record OptionInfo(
    string LongName,
    char? ShortName,
    bool IsFlag,
    string? ValueHint,
    string? Description,
    string? Group,
    IReadOnlyList<string>? Choices = null);
