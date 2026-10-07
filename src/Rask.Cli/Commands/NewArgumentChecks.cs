using System.Globalization;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;
using static Rask.Cli.Commands.BatterySelection;
using static Rask.Cli.Commands.NewCommand;

namespace Rask.Cli.Commands;

/// <summary>The refusals <c>rask new</c> gives before it writes anything: a bad name, a battery the template cannot host, a retired flag.</summary>
internal static class NewArgumentChecks
{
    /// <summary>
    /// Whether <paramref name="templateKey"/> pairs a front end with an ASP.NET host — <c>wasm-hosted</c>
    /// and the TypeScript front-end templates.
    /// </summary>
    /// <remarks>
    /// The wire between the two halves is CQRS, so CQRS is the template rather than a battery in it, and
    /// each generator forces it back on.
    /// </remarks>
    internal static bool IsFrontEndPlusHost(string templateKey) =>
        string.Equals(templateKey, WasmHostedKey, StringComparison.Ordinal)
        || SpaFramework.TryGet(templateKey, out _);

    /// <summary>Why the project name on this command line cannot be used as given, or null when it can.</summary>
    internal static string? NameArgumentError(ParsedArguments parsed)
    {
        // A second positional is almost always an unquoted multi-word name — `rask new My App`. Taking the
        // first and dropping the rest would scaffold a project called "My" and say nothing, which is the
        // worst outcome available: silent, wrong, and only noticed after the files are on disk. Every other
        // command in this CLI rejects a stray positional; this one used to be the exception.
        if (parsed.Positionals.Count > 1)
        {
            var joined = string.Concat(parsed.Positionals);
            return $"'rask new' takes one project name, but got {parsed.Positionals.Count.ToString(CultureInfo.InvariantCulture)}: "
                + $"{string.Join(", ", parsed.Positionals.Select(p => $"'{p}'"))}. "
                + (Identifiers.IsValidNamespaceName(joined)
                    ? $"A project name can't contain spaces — did you mean '{joined}'?"
                    : "A project name can't contain spaces.");
        }

        // Both spellings of the same answer, disagreeing. Preferring one silently means the command did
        // something the user can read the opposite of straight off their own command line.
        if (parsed.Option("name") is { } named
            && parsed.FirstPositional is { } positional
            && !named.Equals(positional, StringComparison.Ordinal))
        {
            return $"Two different project names given: '{positional}' and --name '{named}'. Pass one.";
        }

        return null;
    }

    /// <summary>Why the <c>--no-*</c> set cannot apply to <paramref name="template" />, or null when it can.</summary>
    internal static string? BatteryFlagError(TemplateInfo template, string[] off)
    {
        // Turning off something this template never had is a mistake worth naming: it means the command
        // line was written against a different template, and silently accepting it would hide that.
        var absent = off.Where(flag => !template.SupportedFlags.Contains(flag))
            .Select(OffFlag)
            .ToArray();
        if (absent.Length > 0)
        {
            var supported = template.SupportedFlags.Count == 0
                ? "(none)"
                : string.Join(", ", template.SupportedFlags.OrderBy(f => f, StringComparer.Ordinal));
            var rejected = string.Join(", ", absent.Select(f => "--" + f));
            return $"Template '{template.Key}' has nothing to change for: {rejected}. It supports: {supported}.";
        }

        // CQRS is the wire between a front end and its host, so there is no project left without it. Refused
        // rather than ignored, for the same reason --tailwind is below: a flag the CLI accepts and then
        // disregards is the most expensive kind to discover.
        if (off.Contains("cqrs") && IsFrontEndPlusHost(template.Key))
        {
            var wire = string.Equals(template.Key, WasmHostedKey, StringComparison.Ordinal)
                ? "the browser half dispatches through it over Rask.Cqrs.Client"
                : "the generated TypeScript client dispatches through it";
            return $"Template '{template.Key}' can't drop CQRS — {wire}, so it is the template rather than a "
                + "battery in it.";
        }

        return null;
    }

    /// <summary>Why <c>--islands</c> cannot apply here, or null when it can (or was not asked for).</summary>
    internal static string? IslandsError(TemplateInfo template, IReadOnlyList<string> islands)
    {
        if (islands.Count == 0)
        {
            return null;
        }

        // Islands put a front-end component inside a C# page, and these templates have no C# pages.
        if (SpaFramework.TryGet(template.Key, out _))
        {
            return $"--islands is not available on --template {template.Key}: its whole client IS a front end. "
                + "Use the server or wasm template, or add a component to the client you already have.";
        }

        return IslandRuntimes.Refuse(islands);
    }

    /// <summary>
    /// The flags that used to turn a battery <em>on</em>, and are gone now that every battery is on by
    /// default — mapped to the answer that says so.
    /// </summary>
    /// <remarks>
    /// Rejected with a targeted message rather than kept as a silent no-op. A flag the CLI accepts and
    /// then disregards is this repository's most expensive bug class — it is what <c>--template native</c>
    /// did, and the reason <c>--tailwind</c> is refused on the WASM templates instead of ignored.
    /// </remarks>
    internal static string? RetiredFlagError(IReadOnlyList<string> args) =>
        args
            .Where(arg => arg.StartsWith("--", StringComparison.Ordinal))
            // `--flag=false` is a spelling the parser accepts, so match the prefix too rather than only
            // the bare token.
            .Select(arg => RetiredFlagMessage(arg[2..].Split('=')[0]))
            .FirstOrDefault(message => message is not null);

    internal static string? RetiredFlagMessage(string name)
    {
        // Both named an answer on an axis that no longer exists. Tailwind is not an option a project
        // picks, it is what a Rask project is styled with — so there is nothing left for either flag
        // to mean, and someone's muscle memory still has them in it.
        if (name.Equals("tailwind", StringComparison.Ordinal))
        {
            return "--tailwind is gone: Tailwind is built in, so every project is scaffolded with it.";
        }

        if (name.Equals("bootstrap", StringComparison.Ordinal))
        {
            return "--bootstrap is gone: Rask.Bootstrap has been removed and every project is styled "
                + "with Tailwind, which is built in.";
        }

        if (name.Equals("auth", StringComparison.Ordinal))
        {
            return "--auth is gone: every app with a database has accounts now. Register, sign in and "
                + "sign out work out of the box, and /login, /register and /logout are already routed. "
                + "To do without them, write app.Configure(c => c.Auth.Off()) in Program.cs.";
        }

        if (name.Equals("all-batteries", StringComparison.Ordinal))
        {
            return "--all-batteries is gone: every battery is on by default now. "
                + "Pass --no-<battery> to leave one out, e.g. --no-push.";
        }

        // Ahead of the general case, which would say "on by default now" and send the reader looking
        // for a --no- that no longer exists either.
        if (name.Equals("localization", StringComparison.Ordinal)
            || name.Equals("no-localization", StringComparison.Ordinal)
            || name.Equals("culture", StringComparison.Ordinal))
        {
            return $"--{name} is gone: the languages an app ships are configured in Program.cs, not on "
                + "this command line. A new project starts with English, and adding a language is a "
                + "line in the AddRask(configureCulture: ...) call it already has — see "
                + "docs/localization.md.";
        }

        if (Array.IndexOf(BatteryFlags, name) >= 0)
        {
            return $"--{name} is on by default now, so there is nothing to turn on. "
                + $"Pass --no-{name} to leave it out.";
        }

        return null;
    }
}
