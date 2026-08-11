using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Rask.Cli;

/// <summary>
/// Renders the CLI's help pages — the top-level command list and a per-command page (summary, usage,
/// arguments, an aligned options table, and examples). Options come straight from each command's
/// <see cref="ArgumentSchema.Declared"/> list, so help documents exactly what parses.
/// <para>
/// The two-column blocks are Spectre grids: the label column never wraps, the description column does,
/// so a long description folds under itself instead of running off the terminal. Help renders to either
/// stream — stdout for <c>--help</c>, stderr for usage-on-error — and each carries its own color state,
/// which is why the renderer is passed in rather than picked here.
/// </para>
/// </summary>
internal static class CommandHelp
{
    /// <summary>The top-level <c>rask</c> usage: tagline, command list, and footer hints.</summary>
    public static void RenderTopLevel(IConsole console, IReadOnlyList<CliCommand> commands, bool toError)
    {
        var ansi = toError ? console.AnsiError : console.Ansi;

        // On a terminal that can draw it, the logo replaces the plain wordmark line. Piped — or on a
        // console that would mangle the glyphs — the line stays exactly what it has always been, so
        // anything grepping the first line of `rask` still finds it.
        if (!toError && Branding.CanDecorate(console))
        {
            Branding.Write(console, "the Rask framework command-line tool");
        }
        else
        {
            ansi.Write(new Text("rask", ConsoleStyling.Of(ConsoleStyle.Heading)));
            ansi.WriteLine(" — the Rask framework command-line tool");
            ansi.WriteLine();
        }

        ansi.Write(new Text("Usage: "));
        ansi.WriteLine("rask <command> [options]", ConsoleStyling.Of(ConsoleStyle.Code));
        ansi.WriteLine();
        ansi.WriteLine("Commands:", ConsoleStyling.Of(ConsoleStyle.Heading));

        var list = NewGrid();
        foreach (var command in commands)
        {
            list.AddRow(Code(command.Name), new Text(command.Summary));
        }

        Write(ansi, list);

        ansi.WriteLine();
        ansi.Write(new Text("Run '"));
        ansi.Write(new Text("rask <command> --help", ConsoleStyling.Of(ConsoleStyle.Code)));
        ansi.WriteLine("' for command-specific usage.");
        ansi.WriteLine();
        ansi.WriteLine("Options:", ConsoleStyling.Of(ConsoleStyle.Heading));

        var builtIns = NewGrid();
        builtIns.AddRow(Code("--version"), new Text("Show the tool version."));
        builtIns.AddRow(Code("--help"), new Text("Show help for a command."));
        Write(ansi, builtIns);
    }

    /// <summary>A single command's help page: summary, usage, arguments, options, examples.</summary>
    public static void RenderCommand(IConsole console, CliCommand command)
    {
        var ansi = console.Ansi;

        ansi.WriteLine(command.Summary);
        ansi.WriteLine();
        ansi.Write(new Text("Usage: "));
        ansi.WriteLine(command.Usage, ConsoleStyling.Of(ConsoleStyle.Code));

        if (command.Arguments.Count > 0)
        {
            ansi.WriteLine();
            ansi.WriteLine("Arguments:", ConsoleStyling.Of(ConsoleStyle.Heading));

            var arguments = NewGrid();
            foreach (var (name, description) in command.Arguments)
            {
                arguments.AddRow(Code(name), new Text(description));
            }

            Write(ansi, arguments);
        }

        RenderActions(ansi, command.OptionSchema);
        RenderOptions(ansi, command.OptionSchema);

        if (command.Examples.Count > 0)
        {
            ansi.WriteLine();
            ansi.WriteLine("Examples:", ConsoleStyling.Of(ConsoleStyle.Heading));

            var examples = NewGrid();
            foreach (var example in command.Examples)
            {
                examples.AddRow(Code(example), Text.Empty);
            }

            Write(ansi, examples);
        }
    }

    /// <summary>The command's subcommands and their aliases, straight from the schema that dispatches them.</summary>
    private static void RenderActions(IAnsiConsole ansi, ArgumentSchema? schema)
    {
        if (schema is null || schema.Verbs.Count == 0)
        {
            return;
        }

        ansi.WriteLine();
        ansi.WriteLine("Actions:", ConsoleStyling.Of(ConsoleStyle.Heading));

        var grid = NewGrid();
        foreach (var verb in schema.Verbs)
        {
            grid.AddRow(Code(VerbLabel(verb)), new Text(verb.Description));
        }

        Write(ansi, grid);
    }

    private static string VerbLabel(VerbInfo verb) =>
        verb.Aliases.Count == 0 ? verb.Name : $"{verb.Name} ({string.Join(", ", verb.Aliases)})";

    private static void RenderOptions(IAnsiConsole ansi, ArgumentSchema? schema)
    {
        if (schema is null || schema.Declared.Count == 0)
        {
            return;
        }

        ansi.WriteLine();
        ansi.WriteLine("Options:", ConsoleStyling.Of(ConsoleStyle.Heading));

        // One grid for every group, so the label column is sized once and the descriptions stay aligned
        // down the whole page. Group headings are rows in that same column rather than separate blocks,
        // which is what keeps the alignment shared.
        var grid = NewGrid();
        foreach (var option in schema.Declared.Where(o => o.Group is null))
        {
            AddOption(grid, option);
        }

        foreach (var group in schema.Declared.Where(o => o.Group is not null).GroupBy(o => o.Group!, StringComparer.Ordinal))
        {
            grid.AddEmptyRow();
            grid.AddRow(new Text(group.Key + ":", ConsoleStyling.Of(ConsoleStyle.Heading)), Text.Empty);
            foreach (var option in group)
            {
                AddOption(grid, option);
            }
        }

        Write(ansi, grid);
    }

    private static void AddOption(Grid grid, OptionInfo option)
    {
        // Closed sets are listed after the description rather than inside the value hint, which would
        // widen the aligned label column for every other option on the page.
        var choices = option.Choices is { Count: > 0 } c ? $" [{string.Join("|", c)}]" : string.Empty;
        grid.AddRow(Code(Label(option)), new Text((option.Description ?? string.Empty) + choices));
    }

    /// <summary>The left-column label, e.g. <c>-t, --template &lt;value&gt;</c> or <c>    --auth</c>.</summary>
    private static string Label(OptionInfo option)
    {
        var builder = new StringBuilder();
        builder.Append(option.ShortName is char c ? $"-{c}, " : "    ");
        builder.Append("--").Append(option.LongName);
        if (!option.IsFlag)
        {
            builder.Append(" <").Append(option.ValueHint ?? "value").Append('>');
        }

        return builder.ToString();
    }

    private static Text Code(string text) => new(text, ConsoleStyling.Of(ConsoleStyle.Code));

    /// <summary>
    /// A label/description grid: the label is held on one line so the columns line up, the description
    /// wraps. Three spaces between them, matching the spacing the rest of the tool's output uses.
    /// </summary>
    private static Grid NewGrid()
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(3));
        grid.AddColumn(new GridColumn().PadRight(0));
        return grid;
    }

    /// <summary>Write a help block indented two spaces, the indent every list in the help pages uses.</summary>
    private static void Write(IAnsiConsole ansi, IRenderable block) =>
        ansi.Write(new RaggedRight(new Padder(block, new Padding(2, 0, 0, 0))));
}
