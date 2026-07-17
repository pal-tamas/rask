namespace Rask.Cli;

/// <summary>
/// A tiny, dependency-free interactive prompt over <see cref="IConsole"/>. It reads from the console's
/// input stream and is only meant to run when <see cref="Interactive"/> is true (a real terminal); when
/// stdin is redirected/piped, callers should skip prompting and keep their non-interactive behavior. All
/// reads are EOF-safe — a closed stream returns the default rather than looping — so a command can never
/// hang. In tests, <c>StringConsole.InputLines</c> scripts the answers and flips the console to interactive.
/// </summary>
internal sealed class Prompt(IConsole console)
{
    /// <summary>True when stdin is a terminal — the only time a command should prompt.</summary>
    public bool Interactive => !console.IsInputRedirected;

    /// <summary>
    /// Ask for a line of text. With a <paramref name="default"/>, an empty answer accepts it; without one
    /// the question repeats until non-empty (or the input ends, which yields empty and lets the caller fail).
    /// </summary>
    public string Ask(string label, string? @default = null)
    {
        while (true)
        {
            console.Out.Write(@default is null ? $"{label}: " : $"{label} [{@default}]: ");
            console.Out.Flush();

            var line = console.In.ReadLine();
            if (line is null)
            {
                return @default ?? string.Empty; // EOF — don't loop forever.
            }

            line = line.Trim();
            if (line.Length > 0)
            {
                return line;
            }

            if (@default is not null)
            {
                return @default;
            }
        }
    }

    /// <summary>Ask a yes/no question. An empty answer accepts <paramref name="default"/>; EOF returns it.</summary>
    public bool Confirm(string label, bool @default)
    {
        var hint = @default ? "[Y/n]" : "[y/N]";
        while (true)
        {
            console.Out.Write($"{label} {hint} ");
            console.Out.Flush();

            var line = console.In.ReadLine();
            if (line is null)
            {
                return @default;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                return @default;
            }

            if (line.Equals("y", StringComparison.OrdinalIgnoreCase) || line.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (line.Equals("n", StringComparison.OrdinalIgnoreCase) || line.Equals("no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Choose one of <paramref name="options"/> (value + label) by number or by typing the value. An empty
    /// answer or EOF accepts <paramref name="default"/>.
    /// </summary>
    public string Select(string label, IReadOnlyList<(string Value, string Label)> options, string @default)
    {
        console.Out.WriteLine($"{label}:");
        for (var i = 0; i < options.Count; i++)
        {
            var marker = options[i].Value.Equals(@default, StringComparison.Ordinal) ? " (default)" : string.Empty;
            console.Out.WriteLine($"  {i + 1}) {options[i].Label}{marker}");
        }

        while (true)
        {
            console.Out.Write($"Choose [1-{options.Count}]: ");
            console.Out.Flush();

            var line = console.In.ReadLine();
            if (line is null)
            {
                return @default;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                return @default;
            }

            if (int.TryParse(line, out var index) && index >= 1 && index <= options.Count)
            {
                return options[index - 1].Value;
            }

            foreach (var option in options)
            {
                if (option.Value.Equals(line, StringComparison.OrdinalIgnoreCase))
                {
                    return option.Value;
                }
            }
        }
    }
}
