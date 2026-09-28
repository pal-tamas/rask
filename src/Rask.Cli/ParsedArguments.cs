namespace Rask.Cli;

/// <summary>
/// The parsed form of a command's arguments: positionals, valued <c>--options</c>, boolean
/// <c>--flags</c>, anything after a <c>--</c> separator (passthrough), plus collected errors.
/// </summary>
internal sealed class ParsedArguments(
    IReadOnlyList<string> positionals,
    IReadOnlyDictionary<string, string> options,
    IReadOnlyDictionary<string, IReadOnlyList<string>> multiOptions,
    IReadOnlySet<string> flags,
    IReadOnlyList<string> passthrough,
    IReadOnlyList<string> errors)
{
    public IReadOnlyList<string> Positionals { get; } = positionals;

    /// <summary>The first positional argument, or null when there is none.</summary>
    public string? FirstPositional => Positionals.Count > 0 ? Positionals[0] : null;

    public IReadOnlyDictionary<string, string> Options { get; } = options;

    public IReadOnlyDictionary<string, IReadOnlyList<string>> MultiOptions { get; } = multiOptions;

    public IReadOnlySet<string> Flags { get; } = flags;

    public IReadOnlyList<string> Passthrough { get; } = passthrough;

    public IReadOnlyList<string> Errors { get; } = errors;

    public bool HasErrors => Errors.Count > 0;

    public bool HasFlag(string longName) => Flags.Contains(longName);

    public string? Option(string longName) => Options.TryGetValue(longName, out var value) ? value : null;

    /// <summary>All values supplied for a repeatable <see cref="ArgumentSchema.MultiOption"/> (empty if none).</summary>
    public IReadOnlyList<string> MultiOption(string longName) =>
        MultiOptions.TryGetValue(longName, out var values) ? values : [];
}
