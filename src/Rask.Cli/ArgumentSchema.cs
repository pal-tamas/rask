namespace Rask.Cli;

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
        var match = _verbs.Find(verb =>
            verb.Name.Equals(token, StringComparison.Ordinal) || verb.Aliases.Contains(token, StringComparer.Ordinal));
        name = match?.Name ?? string.Empty;
        return match is not null;
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
        var state = new ParseState();
        var i = 0;
        while (i < args.Count)
        {
            var token = args[i];
            if (string.Equals(token, "--", StringComparison.Ordinal))
            {
                state.Passthrough.AddRange(args.Skip(i + 1));
                break;
            }

            if (IsOptionToken(token))
            {
                i = ParseOption(args, i, state);
            }
            else
            {
                state.Positionals.Add(token);
            }

            i++;
        }

        return state.ToParsed();
    }

    /// <summary>Parses the option at <paramref name="i"/>; returns the index of the last token it consumed.</summary>
    private int ParseOption(IReadOnlyList<string> args, int i, ParseState state)
    {
        var token = args[i];
        var (isLong, body, inlineValue) = SplitOptionToken(token);
        if (!_aliases.TryGetValue(body, out var longName))
        {
            // Only long tokens get a suggestion: a mistyped single letter is as likely to be a
            // different option as a typo of this one, so guessing there would be noise.
            var near = isLong ? Suggest.Closest(body, _declared.Select(o => o.LongName)) : null;
            state.Errors.Add(near is null
                ? $"Unknown option '{token}'."
                : $"Unknown option '{token}'. Did you mean '--{near}'?");
            return i;
        }

        if (_flags.Contains(longName))
        {
            ApplyFlag(longName, inlineValue, state.Flags, state.Errors);
            return i;
        }

        // A valued option: take the inline value, else consume the next token — but never
        // swallow a following option/flag (e.g. '--output --auth' must not set output="--auth"
        // and silently drop --auth). Such a case is a missing value, not a value.
        var value = inlineValue;
        if (value is null)
        {
            if (i + 1 >= args.Count || IsOptionToken(args[i + 1]))
            {
                state.Errors.Add($"Option '--{longName}' requires a value.");
                return i;
            }

            value = args[++i];
        }

        if (!TryNormalizeChoice(longName, ref value, state.Errors))
        {
            return i;
        }

        if (!_multiOptions.Contains(longName))
        {
            state.Options[longName] = value;
            return i;
        }

        if (!state.MultiOptions.TryGetValue(longName, out var values))
        {
            state.MultiOptions[longName] = values = [];
        }

        values.Add(value);
        return TakeChoiceRun(args, i, longName, values, state.Errors);
    }

    /// <summary><c>--name=value</c> or <c>-n</c> as the name and the inline value, if any.</summary>
    private static (bool IsLong, string Name, string? InlineValue) SplitOptionToken(string token)
    {
        var isLong = token.StartsWith("--", StringComparison.Ordinal);
        var body = isLong ? token[2..] : token[1..];
        var equals = body.IndexOf('=', StringComparison.Ordinal);
        return equals >= 0
            ? (isLong, body[..equals], body[(equals + 1)..])
            : (isLong, body, null);
    }

    /// <summary>
    /// A multi-option that declares CHOICES also takes several values in a row, so
    /// <c>--islands react angular blazor</c> reads the way it is written rather than forcing the
    /// flag to be repeated three times. Bounded by the choice list: consumption stops at the
    /// first token that is not one, which is what keeps <c>rask new --islands react Shop</c> from
    /// swallowing the app's name. Without declared choices there is nothing to stop on, so
    /// the option stays strictly one-value-per-occurrence.
    /// </summary>
    private int TakeChoiceRun(IReadOnlyList<string> args, int i, string longName, List<string> values, List<string> errors)
    {
        var allowed = _declared
            .FirstOrDefault(o => o.LongName.Equals(longName, StringComparison.Ordinal))?.Choices;

        if (allowed is null)
        {
            return i;
        }

        while (i + 1 < args.Count
            && !IsOptionToken(args[i + 1])
            && allowed.Contains(args[i + 1], StringComparer.OrdinalIgnoreCase))
        {
            var extra = args[++i];
            TryNormalizeChoice(longName, ref extra, errors);
            values.Add(extra);
        }

        return i;
    }

    /// <summary>What <see cref="Parse"/> collects on its way through the tokens.</summary>
    private sealed class ParseState
    {
        public List<string> Positionals { get; } = [];

        public Dictionary<string, string> Options { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<string>> MultiOptions { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

        public List<string> Passthrough { get; } = [];

        public List<string> Errors { get; } = [];

        public ParsedArguments ToParsed() =>
            new(
                Positionals,
                Options,
                MultiOptions.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal),
                Flags,
                Passthrough,
                Errors);
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

        var typed = value;
        if (choices.FirstOrDefault(choice => choice.Equals(typed, StringComparison.OrdinalIgnoreCase)) is { } canonical)
        {
            value = canonical;
            return true;
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
        value.Equals("true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.Ordinal);

    private static bool IsFalse(string value) =>
        value.Equals("false", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "0", StringComparison.Ordinal);
}
