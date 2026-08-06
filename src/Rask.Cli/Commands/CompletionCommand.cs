using System.Text;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask completion &lt;bash|zsh|fish&gt;</c> — print a shell completion script. The script is generated
/// from the live command list and each command's option schema, so it always matches the CLI: add a
/// command or an option and completion follows without a separate list to maintain.
/// </summary>
internal sealed class CompletionCommand(IConsole console, IReadOnlyList<CliCommand> commands) : CliCommand(console)
{
    public override string Name => "completion";

    public override string Summary => "Print a shell completion script (bash, zsh, or fish).";

    public override string Usage => "rask completion <shell>";

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Verb("bash", "A bash completion function.")
            .Verb("zsh", "A zsh completion function.")
            .Verb("fish", "Fish completion definitions.");

    public override IReadOnlyList<string> Examples =>
    [
        "rask completion bash >> ~/.bashrc",
        "rask completion zsh > \"${fpath[1]}/_rask\"",
        "rask completion fish > ~/.config/fish/completions/rask.fish",
    ];

    public override Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (!CreateSchema().TryResolveVerb(args.FirstOrDefault(), out var shell))
        {
            return Task.FromResult(FailUnknownVerb(args.FirstOrDefault(), CreateSchema()));
        }

        // Exclude self from the completed command list — but keep it a valid word so `rask compl<tab>` works.
        var named = commands.Select(c => c.Name).ToArray();
        var script = shell switch
        {
            "bash" => Bash(named),
            "zsh" => Zsh(named),
            _ => Fish(),
        };

        Console.Out.Write(script);
        return Task.FromResult(0);
    }

    private ArgumentSchema? SchemaFor(string commandName) =>
        commands.FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.Ordinal))?.OptionSchema;

    /// <summary>
    /// Every word that can follow a command: its subcommands first, then its options. Verbs come from the
    /// same schema that dispatches them, so <c>rask db &lt;tab&gt;</c> can't drift from what <c>rask db</c> accepts.
    /// </summary>
    private IReadOnlyList<string> WordsFor(string commandName)
    {
        var schema = SchemaFor(commandName);
        if (schema is null)
        {
            return [];
        }

        return
        [
            .. schema.Verbs.Select(v => v.Name),
            .. schema.Declared.Select(o => "--" + o.LongName),
        ];
    }

    /// <summary>The options of <paramref name="commandName"/> that take a value from a closed set.</summary>
    private IEnumerable<OptionInfo> ChoiceOptionsFor(string commandName) =>
        SchemaFor(commandName)?.Declared.Where(o => o.Choices is { Count: > 0 }) ?? [];

    private string Bash(IReadOnlyList<string> commandNames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# rask bash completion — source this file (e.g. add to ~/.bashrc).");
        builder.AppendLine("_rask_complete() {");
        builder.AppendLine("  local cur prev words cword");
        builder.AppendLine("  _get_comp_words_by_ref -n : cur prev words cword 2>/dev/null || { cur=\"${COMP_WORDS[COMP_CWORD]}\"; words=(\"${COMP_WORDS[@]}\"); cword=$COMP_CWORD; }");
        builder.AppendLine($"  local commands=\"{string.Join(' ', commandNames)}\"");
        builder.AppendLine("  if [ \"$cword\" -le 1 ]; then");
        builder.AppendLine("    COMPREPLY=( $(compgen -W \"$commands\" -- \"$cur\") ); return 0");
        builder.AppendLine("  fi");
        builder.AppendLine("  prev=\"${words[cword-1]}\"");
        builder.AppendLine("  case \"${words[1]}\" in");
        foreach (var command in commandNames)
        {
            var words = WordsFor(command);
            if (words.Count == 0)
            {
                continue;
            }

            builder.AppendLine($"    {command})");

            // An option with a closed set completes its own values, keyed off the preceding word — nested
            // inside the command's branch because the same option name means different things elsewhere
            // (`--host` is a set of native modes for `new`, a free-form SSH target for `deploy`).
            foreach (var option in ChoiceOptionsFor(command))
            {
                builder.AppendLine($"      if [ \"$prev\" = \"--{option.LongName}\" ]; then COMPREPLY=( $(compgen -W \"{string.Join(' ', option.Choices!)}\" -- \"$cur\") ); return 0; fi");
            }

            builder.AppendLine($"      COMPREPLY=( $(compgen -W \"{string.Join(' ', words)}\" -- \"$cur\") );;");
        }

        builder.AppendLine("    *) COMPREPLY=( $(compgen -f -- \"$cur\") );;");
        builder.AppendLine("  esac");
        builder.AppendLine("}");
        builder.AppendLine("complete -F _rask_complete rask");
        return builder.ToString();
    }

    private string Zsh(IReadOnlyList<string> commandNames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#compdef rask");
        builder.AppendLine("# rask zsh completion — place on your $fpath as _rask.");
        builder.AppendLine("_rask() {");
        builder.AppendLine("  local -a commands");
        builder.AppendLine($"  commands=({string.Join(' ', commandNames)})");
        builder.AppendLine("  if (( CURRENT <= 2 )); then");
        builder.AppendLine("    compadd -- $commands; return");
        builder.AppendLine("  fi");
        builder.AppendLine("  local prev=\"${words[CURRENT-1]}\"");
        builder.AppendLine("  case \"${words[2]}\" in");
        foreach (var command in commandNames)
        {
            var words = WordsFor(command);
            if (words.Count == 0)
            {
                continue;
            }

            builder.AppendLine($"    {command})");
            foreach (var option in ChoiceOptionsFor(command))
            {
                builder.AppendLine($"      if [[ \"$prev\" == \"--{option.LongName}\" ]]; then compadd -- {string.Join(' ', option.Choices!)}; return; fi");
            }

            builder.AppendLine($"      compadd -- {string.Join(' ', words)} ;;");
        }

        builder.AppendLine("    *) _files ;;");
        builder.AppendLine("  esac");
        builder.AppendLine("}");
        builder.AppendLine("_rask \"$@\"");
        return builder.ToString();
    }

    private string Fish()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# rask fish completion — save as ~/.config/fish/completions/rask.fish.");
        builder.AppendLine("function __rask_no_subcommand");
        builder.AppendLine("  set -l cmd (commandline -opc)");
        builder.AppendLine("  test (count $cmd) -eq 1");
        builder.AppendLine("end");
        foreach (var command in commands)
        {
            var description = command.Summary.Replace("'", "", StringComparison.Ordinal);
            builder.AppendLine($"complete -c rask -f -n __rask_no_subcommand -a {command.Name} -d '{description}'");
        }

        foreach (var command in commands)
        {
            foreach (var verb in command.OptionSchema?.Verbs ?? [])
            {
                var description = verb.Description.Replace("'", "", StringComparison.Ordinal);
                builder.AppendLine($"complete -c rask -f -n '__fish_seen_subcommand_from {command.Name}' -a {verb.Name} -d '{description}'");
            }

            foreach (var option in command.OptionSchema?.Declared ?? [])
            {
                var description = (option.Description ?? string.Empty).Replace("'", "", StringComparison.Ordinal);
                // -r marks an option that takes a value, so fish stops offering it its own siblings; -a
                // supplies that value's completions when the set is closed.
                var takesValue = option.IsFlag ? string.Empty : " -r";
                var values = option.Choices is { Count: > 0 } c ? $" -a '{string.Join(' ', c)}'" : string.Empty;
                builder.AppendLine($"complete -c rask -n '__fish_seen_subcommand_from {command.Name}' -l {option.LongName}{takesValue}{values} -d '{description}'");
            }
        }

        return builder.ToString();
    }
}
