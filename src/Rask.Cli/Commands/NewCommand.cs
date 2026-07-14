using Rask.Cli.Templates;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask new</c> — scaffold a Rask project. Resolves a friendly template name to its <c>dotnet new</c>
/// short name, validates the requested feature flags against that template, ensures the Rask.Templates
/// package is installed, then delegates to <c>dotnet new</c>.
/// </summary>
internal sealed class NewCommand(IConsole console, IProcessRunner process) : CliCommand(console, process)
{
    /// <summary>The opt-in feature flags <c>rask new</c> forwards to a template (as <c>--flag</c>).</summary>
    internal static readonly string[] FeatureFlags = ["auth", "pwa", "cqrs", "docker"];

    public override string Name => "new";

    public override string Summary => "Create a new Rask project from a template.";

    public override string Usage =>
        "rask new <name> [--template server|wasm|wasm-hosted|native] [--auth] [--pwa] [--cqrs] [--docker] [--output <dir>]";

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = new ArgumentSchema()
            .Option("template", 't')
            .Option("output", 'o')
            .Option("name", 'n')
            .Flag("auth")
            .Flag("pwa")
            .Flag("cqrs")
            .Flag("docker");

        var parsed = schema.Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var name = parsed.Option("name") ?? parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("A project name is required.");
            Console.Error.WriteLine($"Usage: {Usage}");
            return 1;
        }

        var templateKey = parsed.Option("template") ?? TemplateCatalog.Default.Key;
        if (!TemplateCatalog.TryGet(templateKey, out var template))
        {
            var available = string.Join(", ", TemplateCatalog.All.Select(t => t.Key));
            Console.Error.WriteLine($"Unknown template '{templateKey}'. Available templates: {available}.");
            return 1;
        }

        var requestedFlags = FeatureFlags.Where(parsed.HasFlag).ToArray();
        var unsupported = requestedFlags.Where(flag => !template.SupportedFlags.Contains(flag)).ToArray();
        if (unsupported.Length > 0)
        {
            var supported = template.SupportedFlags.Count == 0
                ? "(none)"
                : string.Join(", ", template.SupportedFlags.OrderBy(f => f, StringComparer.Ordinal).Select(f => "--" + f));
            var rejected = string.Join(", ", unsupported.Select(f => "--" + f));
            Console.Error.WriteLine($"Template '{template.Key}' does not support: {rejected}. Supported flags: {supported}.");
            return 1;
        }

        var dotnetArgs = BuildDotnetNewArguments(template, name, parsed.Option("output"), requestedFlags);

        if (!await TemplateProbe.AreInstalledAsync(Process, cancellationToken).ConfigureAwait(false))
        {
            Console.Out.WriteLine("Rask templates were not found — installing the Rask.Templates package…");
            var install = await Process.RunAsync("dotnet", ["new", "install", "Rask.Templates"], null, cancellationToken).ConfigureAwait(false);
            if (install != 0)
            {
                Console.Error.WriteLine("Failed to install Rask.Templates. Run 'dotnet new install Rask.Templates' and retry.");
                return install;
            }
        }

        Console.Out.WriteLine($"Creating {template.DisplayName} '{name}'…");
        return await Process.RunAsync("dotnet", dotnetArgs, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Build the <c>dotnet new</c> argument list. Pure and deterministic, so it is unit-tested directly.</summary>
    internal static IReadOnlyList<string> BuildDotnetNewArguments(
        TemplateInfo template, string name, string? output, IReadOnlyList<string> flags)
    {
        var args = new List<string> { "new", template.ShortName, "--name", name };

        if (!string.IsNullOrWhiteSpace(output))
        {
            args.Add("--output");
            args.Add(output);
        }

        foreach (var flag in flags)
        {
            args.Add("--" + flag);
        }

        return args;
    }
}
