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

/// <summary>
/// A declared flag or option: its names, whether it takes a value (and a hint for it), a one-line
/// description, and an optional <see cref="Group"/> label so help can bucket, say, <c>generate</c>'s
/// feature-only flags apart from the common ones. This is the single source of truth that both parses
/// arguments and documents them — <c>--help</c> renders straight from this list, so it can never drift.
/// </summary>
internal sealed record OptionInfo(
    string LongName,
    char? ShortName,
    bool IsFlag,
    string? ValueHint,
    string? Description,
    string? Group);

/// <summary>
/// A tiny, dependency-free argument parser. Each command declares its boolean <see cref="Flag"/>s and
/// valued <see cref="Option"/>s (with optional single-char aliases); <see cref="Parse"/> then turns a
/// raw token list into a <see cref="ParsedArguments"/>. Supports <c>--name value</c>, <c>--name=value</c>,
/// <c>-n value</c>, and a <c>--</c> separator after which everything is passthrough. Unknown options and
/// options missing a value are reported as errors rather than guessed at. Every declaration also records
/// an <see cref="OptionInfo"/> (see <see cref="Declared"/>) so command help documents exactly what parses.
/// </summary>
internal sealed class ArgumentSchema
{
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);
    private readonly HashSet<string> _options = new(StringComparer.Ordinal);
    private readonly HashSet<string> _multiOptions = new(StringComparer.Ordinal);
    private readonly List<OptionInfo> _declared = [];

    /// <summary>Every flag/option declared on this schema, in declaration order — the source for <c>--help</c>.</summary>
    public IReadOnlyList<OptionInfo> Declared => _declared;

    public ArgumentSchema Flag(string longName, char? shortName = null, string? description = null, string? group = null)
    {
        _flags.Add(longName);
        Register(longName, shortName);
        _declared.Add(new OptionInfo(longName, shortName, IsFlag: true, ValueHint: null, description, group));
        return this;
    }

    public ArgumentSchema Option(string longName, char? shortName = null, string? valueHint = null, string? description = null, string? group = null)
    {
        _options.Add(longName);
        Register(longName, shortName);
        _declared.Add(new OptionInfo(longName, shortName, IsFlag: false, valueHint, description, group));
        return this;
    }

    /// <summary>
    /// A valued option that may be supplied more than once (e.g. <c>--env A=1 --env B=2</c>); every value is
    /// collected in order and read via <see cref="ParsedArguments.MultiOption"/> rather than overwriting.
    /// </summary>
    public ArgumentSchema MultiOption(string longName, char? shortName = null, string? valueHint = null, string? description = null, string? group = null)
    {
        _options.Add(longName);
        _multiOptions.Add(longName);
        Register(longName, shortName);
        _declared.Add(new OptionInfo(longName, shortName, IsFlag: false, valueHint, description, group));
        return this;
    }

    private void Register(string longName, char? shortName)
    {
        _aliases[longName] = longName;
        if (shortName is char c)
        {
            _aliases[c.ToString()] = longName;
        }
    }

    public ParsedArguments Parse(IReadOnlyList<string> args)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var multiOptions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var passthrough = new List<string>();
        var errors = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            if (token == "--")
            {
                for (var j = i + 1; j < args.Count; j++)
                {
                    passthrough.Add(args[j]);
                }

                break;
            }

            if (!IsOptionToken(token))
            {
                positionals.Add(token);
                continue;
            }

            var isLong = token.StartsWith("--", StringComparison.Ordinal);
            var body = isLong ? token[2..] : token[1..];
            string? inlineValue = null;
            var equals = body.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                inlineValue = body[(equals + 1)..];
                body = body[..equals];
            }

            if (!_aliases.TryGetValue(body, out var longName))
            {
                errors.Add($"Unknown option '{token}'.");
                continue;
            }

            if (_flags.Contains(longName))
            {
                ApplyFlag(longName, inlineValue, flags, errors);
                continue;
            }

            // A valued option: take the inline value, else consume the next token — but never
            // swallow a following option/flag (e.g. '--output --auth' must not set output="--auth"
            // and silently drop --auth). Such a case is a missing value, not a value.
            var value = inlineValue;
            if (value is null)
            {
                if (i + 1 < args.Count && !IsOptionToken(args[i + 1]))
                {
                    value = args[++i];
                }
                else
                {
                    errors.Add($"Option '--{longName}' requires a value.");
                    continue;
                }
            }

            if (_multiOptions.Contains(longName))
            {
                if (!multiOptions.TryGetValue(longName, out var values))
                {
                    multiOptions[longName] = values = [];
                }

                values.Add(value);
            }
            else
            {
                options[longName] = value;
            }
        }

        var multi = multiOptions.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);

        return new ParsedArguments(positionals, options, multi, flags, passthrough, errors);
    }

    private static void ApplyFlag(string longName, string? inlineValue, HashSet<string> flags, List<string> errors)
    {
        if (inlineValue is null || IsTrue(inlineValue))
        {
            flags.Add(longName);
        }
        else if (IsFalse(inlineValue))
        {
            flags.Remove(longName);
        }
        else
        {
            errors.Add($"Flag '--{longName}' does not accept the value '{inlineValue}'.");
        }
    }

    private static bool IsOptionToken(string token) =>
        token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]);

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static bool IsFalse(string value) =>
        value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0";
}
