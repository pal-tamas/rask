using System.Text;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask completion &lt;bash|zsh|fish&gt;</c> — print a shell completion script. The script is generated
/// from the live command list and each command's option schema, so it always matches the CLI: add a
/// command or an option and completion follows without a separate list to maintain.
/// </summary>
internal sealed class CompletionCommand(IConsole console, IReadOnlyList<CliCommand> commands) : CliCommand(console)
{
    private static readonly string[] Shells = ["bash", "zsh", "fish"];

    public override string Name => "completion";

    public override string Summary => "Print a shell completion script (bash, zsh, or fish).";

    public override string Usage => "rask completion <bash|zsh|fish>";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
        [("<bash|zsh|fish>", "The shell to generate a completion script for.")];

    public override IReadOnlyList<string> Examples =>
    [
        "rask completion bash >> ~/.bashrc",
        "rask completion zsh > \"${fpath[1]}/_rask\"",
        "rask completion fish > ~/.config/fish/completions/rask.fish",
    ];

    public override Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var shell = args.Count > 0 ? args[0] : null;
        if (shell is null || !Shells.Contains(shell))
        {
            Console.Error.WriteLine($"Specify a shell: {string.Join(", ", Shells)}.");
            Console.Error.WriteLine($"Usage: {Usage}");
            return Task.FromResult(1);
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

    private IReadOnlyList<string> OptionsFor(string commandName)
    {
        var command = commands.FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.Ordinal));
        return command?.OptionSchema?.Declared.Select(o => "--" + o.LongName).ToArray() ?? [];
    }

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
        builder.AppendLine("  case \"${words[1]}\" in");
        foreach (var command in commandNames)
        {
            var options = OptionsFor(command);
            if (options.Count > 0)
            {
                builder.AppendLine($"    {command}) COMPREPLY=( $(compgen -W \"{string.Join(' ', options)}\" -- \"$cur\") );;");
            }
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
        builder.AppendLine("  case \"${words[2]}\" in");
        foreach (var command in commandNames)
        {
            var options = OptionsFor(command);
            if (options.Count > 0)
            {
                builder.AppendLine($"    {command}) compadd -- {string.Join(' ', options)} ;;");
            }
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
            foreach (var option in command.OptionSchema?.Declared ?? [])
            {
                var description = (option.Description ?? string.Empty).Replace("'", "", StringComparison.Ordinal);
                builder.AppendLine($"complete -c rask -n '__fish_seen_subcommand_from {command.Name}' -l {option.LongName} -d '{description}'");
            }
        }

        return builder.ToString();
    }
}
