using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;
using Spectre.Console;
using static Rask.Cli.Commands.BatterySelection;

namespace Rask.Cli.Commands;

internal sealed partial class NewCommand
{
    /// <summary>
    /// Walk the interactive first-run flow (name → template → batteries) and return the
    /// equivalent argument list, so the answers flow back through the exact same validation and generation
    /// path as a fully-typed command line. Only reached on a terminal.
    /// <para>
    /// The wizard <b>fills gaps, it does not re-ask</b>: whatever the command line already answered is
    /// kept verbatim and its question is skipped, so <c>rask new --template wasm</c> asks for a name and
    /// nothing else. Questions are also skipped when they cannot apply — no snapshots question on a
    /// template with no database.
    /// </para>
    /// </summary>
    private List<string> RunWizard(Prompt prompt, IReadOnlyList<string> args, ParsedArguments parsed)
    {
        Branding.Write(Console, "let's set up your project");
        var ansi = Console.Ansi;
        ansi.Write(new Rule().RuleStyle("dim"));
        ansi.WriteLine();

        // Everything already typed stands; the wizard only appends what is still unanswered.
        var filled = new List<string>(args);

        if (string.IsNullOrWhiteSpace(parsed.Option("name") ?? parsed.FirstPositional))
        {
            // Validate here rather than after the answers are re-parsed: being told the name is unusable
            // while still in the question is a correction, being told it afterwards is a restart.
            filled.Insert(0, prompt.Ask(
                "Project name",
                validate: value => Identifiers.IsValidNamespaceName(value)
                    ? null
                    : $"'{value}' can't be a root namespace — start each dot-separated part with a letter or underscore (e.g. Shop or Contoso.Shop)."));
        }

        var templateKey = parsed.Option("template");
        if (templateKey is null)
        {
            templateKey = prompt.Select(
                "Project type",
                [.. TemplateCatalog.All.Select(t => (t.Key, $"[bold]{t.Key}[/] [dim]— {t.DisplayName}[/]"))],
                TemplateCatalog.Default.Key);

            filled.Add("--template");
            filled.Add(templateKey);
        }

        _ = TemplateCatalog.TryGet(templateKey, out var template);

        // Styling asks nothing: Tailwind is built in, so there is no answer a project could give.

        // The browser no longer gets a question of its own: it is a project TYPE now (wasm-hosted, beside
        // wasm and the front-end templates), so it is answered by the list above rather than by a yes/no
        // afterwards (#1103). That also keeps the checklist below honest — it is about taking things away,
        // and mixing something you ADD into a list of things you remove read as the opposite of what it did.
        // Authentication used to be asked here and is not any more: an app with a database has accounts.

        // The batteries are NOT asked about. Batteries are included: every one the template supports is on,
        // and the wizard's job is the handful of answers that decide what the app is, not a menu over
        // thirteen things that are all already the right answer. Anyone who wants one gone says so on the
        // command line — `rask new Shop --no-ops` — and the summary below still prints the full list, so
        // what you are getting is stated either way.
        //
        // The checklist that used to stand here arrived fully ticked, which made it a question whose
        // answer was "yes" every time: thirteen rows to read past before the scaffold could start.

        WriteWizardSummary(filled, template, DotnetTarget.For(parsed.Option("framework")));
        return filled;
    }

    /// <summary>
    /// Restate the answers before the files start appearing, so the scaffolding output is read as the
    /// result of a decision rather than as a wall of paths.
    /// </summary>
    /// <remarks>
    /// Only rows that were actually decided are shown. A summary listing every axis would tell a WASM
    /// SPA it had chosen a database battery — a question that template never asks and does not support —
    /// which is worse than saying nothing, because it reads as confirmation.
    /// </remarks>
    private void WriteWizardSummary(List<string> args, TemplateInfo template, DotnetTarget dotnet)
    {
        // Resolved through the same path the scaffold will take, rather than read back off the flags. The
        // summary's whole job is to be what happens next, and a second reading of the same answers is how
        // it comes to say something the generator then contradicts.
        var off = BatteryFlags.Where(f => args.Contains("--" + OffFlag(f), StringComparer.Ordinal)).ToArray();
        var batteries = ToBatteries(template, off);

        var on = BatteryFlags.Where(f => f is not ("docker" or "tests") && Includes(batteries, f)).ToArray();

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        grid.AddRow(Label("📦", "Project"), new Text(args[0]));
        grid.AddRow(Label("🧩", "Type"), new Text(template.DisplayName));

        grid.AddRow(
            Label("🔑", "Auth"),
            new Text(batteries.Data
                ? "accounts — sign in, register, sign out"
                : "none (accounts need a database)"));

        if (batteries.Data)
        {
            grid.AddRow(Label("🗄️", "Database"), new Text("SQLite (one file, no server)"));
        }

        grid.AddRow(Label("🐳", "Docker"), new Text(batteries.Docker ? "yes" : "no"));

        if (template.SupportedFlags.Contains("tests"))
        {
            grid.AddRow(Label("🧪", "Tests"), new Text(batteries.Tests ? "yes — one passing, run with dotnet test" : "no"));
        }

        // Only when it is not the default. The summary's job is to restate the DECISIONS, and .NET 10 is the
        // one nobody made — a row saying so on every scaffold would be one more line to read past, and the
        // wizard deliberately asks nothing about the version. The value comes from the PARSE, which has
        // already normalised the choice, rather than from a second reading of the raw arguments:
        // `--framework NET11.0` scaffolds net11.0, and a case-sensitive re-read would have dropped this row.
        if (dotnet != DotnetTarget.Default)
        {
            grid.AddRow(
                Label("🎯", ".NET"),
                new Text($"{dotnet.Moniker} (the default is {DotnetTarget.Default.Moniker}, the LTS release)"));
        }

        grid.AddRow(
            Label("🔋", "Batteries"),
            new Text(on.Length > 0 ? string.Join(", ", on) : "none"));

        var ansi = Console.Ansi;
        ansi.WriteLine();
        ansi.Write(new RaggedRight(new Padder(grid, new Padding(1, 0, 0, 1))));

        Text Label(string emoji, string text) =>
            new(Branding.Label(Console, emoji, text), ConsoleStyling.Of(ConsoleStyle.Dim));
    }
}
