using System.Text;

namespace Rask.Cli;

/// <summary>
/// Renders the CLI's help pages — the top-level command list and a per-command page (summary, usage,
/// arguments, an aligned options table, and examples). Options come straight from each command's
/// <see cref="ArgumentSchema.Declared"/> list, so help documents exactly what parses. Color is applied
/// only when the target stream is a terminal; everything is written through plain <see cref="TextWriter"/>s
/// with a resolved <c>color</c> flag so both stdout (help) and stderr (usage-on-error) render correctly.
/// </summary>
internal static class CommandHelp
{
    /// <summary>The top-level <c>rask</c> usage: tagline, command list, and footer hints.</summary>
    public static void RenderTopLevel(IConsole console, IReadOnlyList<CliCommand> commands, bool toError)
    {
        var writer = toError ? console.Error : console.Out;
        var color = ConsoleStyling.ColorEnabled(toError ? console.IsErrorRedirected : console.IsOutputRedirected);

        writer.WriteLine(Paint("rask", ConsoleStyle.Heading, color) + " — the Rask framework command-line tool");
        writer.WriteLine();
        writer.WriteLine($"Usage: {Paint("rask <command> [options]", ConsoleStyle.Code, color)}");
        writer.WriteLine();
        writer.WriteLine(Paint("Commands:", ConsoleStyle.Heading, color));

        var width = commands.Count == 0 ? 0 : commands.Max(c => c.Name.Length);
        foreach (var command in commands)
        {
            writer.WriteLine($"  {Paint(command.Name.PadRight(width), ConsoleStyle.Code, color)}   {command.Summary}");
        }

        writer.WriteLine();
        writer.WriteLine($"Run '{Paint("rask <command> --help", ConsoleStyle.Code, color)}' for command-specific usage.");
        writer.WriteLine();
        writer.WriteLine(Paint("Options:", ConsoleStyle.Heading, color));
        writer.WriteLine("  --version    Show the tool version.");
        writer.WriteLine("  --help       Show help for a command.");
    }

    /// <summary>A single command's help page: summary, usage, arguments, options, examples.</summary>
    public static void RenderCommand(IConsole console, CliCommand command)
    {
        var writer = console.Out;
        var color = ConsoleStyling.ColorEnabled(console.IsOutputRedirected);

        writer.WriteLine(command.Summary);
        writer.WriteLine();
        writer.WriteLine($"Usage: {Paint(command.Usage, ConsoleStyle.Code, color)}");

        if (command.Arguments.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine(Paint("Arguments:", ConsoleStyle.Heading, color));
            var argWidth = command.Arguments.Max(a => a.Name.Length);
            foreach (var (name, description) in command.Arguments)
            {
                writer.WriteLine($"  {Paint(name.PadRight(argWidth), ConsoleStyle.Code, color)}   {description}");
            }
        }

        RenderActions(writer, command.OptionSchema, color);
        RenderOptions(writer, command.OptionSchema, color);

        if (command.Examples.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine(Paint("Examples:", ConsoleStyle.Heading, color));
            foreach (var example in command.Examples)
            {
                writer.WriteLine($"  {Paint(example, ConsoleStyle.Code, color)}");
            }
        }
    }

    /// <summary>The command's subcommands and their aliases, straight from the schema that dispatches them.</summary>
    private static void RenderActions(TextWriter writer, ArgumentSchema? schema, bool color)
    {
        if (schema is null || schema.Verbs.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine(Paint("Actions:", ConsoleStyle.Heading, color));
        var width = schema.Verbs.Max(v => VerbLabel(v).Length);
        foreach (var verb in schema.Verbs)
        {
            writer.WriteLine($"  {Paint(VerbLabel(verb).PadRight(width), ConsoleStyle.Code, color)}   {verb.Description}");
        }
    }

    private static string VerbLabel(VerbInfo verb) =>
        verb.Aliases.Count == 0 ? verb.Name : $"{verb.Name} ({string.Join(", ", verb.Aliases)})";

    private static void RenderOptions(TextWriter writer, ArgumentSchema? schema, bool color)
    {
        if (schema is null || schema.Declared.Count == 0)
        {
            return;
        }

        // One column width across every group keeps the descriptions aligned down the whole page.
        var width = schema.Declared.Max(o => Label(o).Length);

        var ungrouped = schema.Declared.Where(o => o.Group is null).ToArray();
        var groups = schema.Declared
            .Where(o => o.Group is not null)
            .GroupBy(o => o.Group!, StringComparer.Ordinal);

        writer.WriteLine();
        writer.WriteLine(Paint("Options:", ConsoleStyle.Heading, color));
        foreach (var option in ungrouped)
        {
            WriteOption(writer, option, width, color);
        }

        foreach (var group in groups)
        {
            writer.WriteLine();
            writer.WriteLine($"  {Paint(group.Key + ":", ConsoleStyle.Heading, color)}");
            foreach (var option in group)
            {
                WriteOption(writer, option, width, color);
            }
        }
    }

    private static void WriteOption(TextWriter writer, OptionInfo option, int width, bool color)
    {
        var label = Label(option);
        var painted = Paint(label.PadRight(width), ConsoleStyle.Code, color);

        // Closed sets are listed after the description rather than inside the value hint, which would
        // widen the aligned label column for every other option on the page.
        var choices = option.Choices is { Count: > 0 } c ? $" [{string.Join("|", c)}]" : string.Empty;
        writer.WriteLine(option.Description is { Length: > 0 } d ? $"  {painted}   {d}{choices}" : $"  {painted}{choices}");
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

    private static string Paint(string text, ConsoleStyle style, bool color) =>
        color ? ConsoleStyling.Paint(text, style) : text;
}
