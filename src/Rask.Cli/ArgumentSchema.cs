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
/// description, an optional <see cref="Group"/> label so help can bucket, say, <c>generate</c>'s
/// feature-only flags apart from the common ones, and an optional closed set of <see cref="Choices"/>.
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

/// <summary>
/// A declared subcommand of a command, e.g. <c>add</c> under <c>rask db</c>. Recording verbs on the schema
/// (rather than in a private array per command) means dispatch, the unknown-action error, <c>--help</c>,
/// and shell completion all read the same list — including the aliases, which used to be invisible in
/// both help and errors.
/// </summary>
internal sealed record VerbInfo(string Name, string Description, IReadOnlyList<string> Aliases);

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
    private readonly List<VerbInfo> _verbs = [];

    /// <summary>Every flag/option declared on this schema, in declaration order — the source for <c>--help</c>.</summary>
    public IReadOnlyList<OptionInfo> Declared => _declared;

    /// <summary>Every subcommand declared on this schema, in declaration order.</summary>
    public IReadOnlyList<VerbInfo> Verbs => _verbs;

    public ArgumentSchema Flag(string longName, char? shortName = null, string? description = null, string? group = null)
    {
        _flags.Add(longName);
        Register(longName, shortName);
        _declared.Add(new OptionInfo(longName, shortName, IsFlag: true, ValueHint: null, description, group));
        return this;
    }

    public ArgumentSchema Option(
        string longName,
        char? shortName = null,
        string? valueHint = null,
        string? description = null,
        string? group = null,
        IReadOnlyList<string>? choices = null)
    {
        _options.Add(longName);
        Register(longName, shortName);
        _declared.Add(new OptionInfo(longName, shortName, IsFlag: false, valueHint, description, group, choices));
        return this;
    }

    /// <summary>
    /// Declare a subcommand. <paramref name="aliases"/> resolve to the same verb, so <c>rask g f</c> and
    /// <c>rask db backup</c> take one path and both are documented.
    /// </summary>
    public ArgumentSchema Verb(string name, string description, params string[] aliases)
    {
        _verbs.Add(new VerbInfo(name, description, aliases));
        return this;
    }

    /// <summary>
    /// Resolve a typed token to a declared verb name, following aliases. False when the token names no
    /// verb — the caller reports that through <see cref="CliCommand.FailUnknownVerb"/>.
    /// </summary>
    public bool TryResolveVerb(string? token, out string name)
    {
        foreach (var verb in _verbs)
        {
            if (verb.Name.Equals(token, StringComparison.Ordinal) || verb.Aliases.Contains(token, StringComparer.Ordinal))
            {
                name = verb.Name;
                return true;
            }
        }

        name = string.Empty;
        return false;
    }

    /// <summary>
    /// A valued option that may be supplied more than once (e.g. <c>--env A=1 --env B=2</c>); every value is
    /// collected in order and read via <see cref="ParsedArguments.MultiOption"/> rather than overwriting.
    /// </summary>
    public ArgumentSchema MultiOption(
        string longName,
        char? shortName = null,
        string? valueHint = null,
        string? description = null,
        string? group = null,
        IReadOnlyList<string>? choices = null)
    {
        _options.Add(longName);
        _multiOptions.Add(longName);
        Register(longName, shortName);
        _declared.Add(new OptionInfo(longName, shortName, IsFlag: false, valueHint, description, group, choices));
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
                // Only long tokens get a suggestion: a mistyped single letter is as likely to be a
                // different option as a typo of this one, so guessing there would be noise.
                var near = isLong ? Suggest.Closest(body, _declared.Select(o => o.LongName)) : null;
                errors.Add(near is null
                    ? $"Unknown option '{token}'."
                    : $"Unknown option '{token}'. Did you mean '--{near}'?");
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

            if (!TryNormalizeChoice(longName, ref value, errors))
            {
                continue;
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

    /// <summary>
    /// Check a value against the option's declared <see cref="OptionInfo.Choices"/>, if it has any, and
    /// rewrite it to the declared spelling so <c>--template SERVER</c> reaches the command as
    /// <c>server</c> — every consumer downstream compares ordinally.
    /// <para>
    /// One phrasing for every closed-set option in the CLI, naming both the nearest match and the whole
    /// set: the list is short by definition, so printing it beats sending the reader to <c>--help</c>.
    /// </para>
    /// </summary>
    private bool TryNormalizeChoice(string longName, ref string value, List<string> errors)
    {
        var choices = _declared.FirstOrDefault(o => o.LongName.Equals(longName, StringComparison.Ordinal))?.Choices;
        if (choices is null)
        {
            return true;
        }

        foreach (var choice in choices)
        {
            if (choice.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                value = choice;
                return true;
            }
        }

        var near = Suggest.Closest(value, choices);
        var didYouMean = near is null ? string.Empty : $" Did you mean '{near}'?";
        errors.Add($"Option '--{longName}' does not accept '{value}'.{didYouMean} Choose one of: {string.Join(", ", choices)}.");
        return false;
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
