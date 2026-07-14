using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask generate</c> — scaffold source into the current project. Locates the owning <c>.csproj</c>,
/// derives the folder-based namespace, and writes idiomatic files for the requested artifact
/// (<c>page</c>, <c>component</c>, or a full CRUD <c>feature</c>), refusing to clobber an existing file
/// unless <c>--force</c>.
/// </summary>
internal sealed class GenerateCommand(IConsole console, IFileSystem fileSystem, string workingDirectory)
    : CliCommand(console)
{
    private static readonly string[] Kinds = ["page", "component", "feature"];

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "generate";

    public override string Summary => "Scaffold a page, component, or CRUD feature into the current project.";

    public override string Usage =>
        "rask generate <page|component|feature> <Name> [--fields \"Name:type,...\"] [--route <path>] [--context <Name>] [--plural <Name>] [--output <dir>] [--force] [--dry-run]";

    public override Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine($"Specify what to generate: {string.Join(", ", Kinds)}.");
            Console.Error.WriteLine($"Usage: {Usage}");
            return Task.FromResult(1);
        }

        var kind = args[0];
        if (!Kinds.Contains(kind))
        {
            Console.Error.WriteLine($"Unknown artifact '{kind}'. Generate one of: {string.Join(", ", Kinds)}.");
            return Task.FromResult(1);
        }

        var schema = new ArgumentSchema()
            .Option("route", 'r')
            .Option("output", 'o')
            .Option("fields", 'f')
            .Option("context", 'c')
            .Option("plural", 'p')
            .Flag("force")
            .Flag("dry-run");

        var parsed = schema.Parse(args.Skip(1).ToArray());
        if (parsed.HasErrors)
        {
            return Task.FromResult(Fail(parsed.Errors));
        }

        var name = parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine($"A name is required. Usage: {Usage}");
            return Task.FromResult(1);
        }

        if (!Identifiers.IsValidTypeName(name))
        {
            Console.Error.WriteLine($"'{name}' is not a valid C# type name (letters, digits, and '_'; not starting with a digit).");
            return Task.FromResult(1);
        }

        var route = parsed.Option("route");
        if (route is not null)
        {
            if (kind != "page")
            {
                Console.Error.WriteLine("--route only applies to 'generate page'.");
                return Task.FromResult(1);
            }

            if (!Identifiers.IsValidRoutePath(route))
            {
                Console.Error.WriteLine($"'{route}' is not a valid route path (no quotes, backslashes, or control characters).");
                return Task.FromResult(1);
            }
        }

        if (kind != "feature" && (parsed.Option("fields") is not null || parsed.Option("context") is not null || parsed.Option("plural") is not null))
        {
            Console.Error.WriteLine("--fields, --context, and --plural only apply to 'generate feature'.");
            return Task.FromResult(1);
        }

        foreach (var (option, value) in new[] { ("context", parsed.Option("context")), ("plural", parsed.Option("plural")) })
        {
            if (value is not null && !Identifiers.IsValidTypeName(value))
            {
                Console.Error.WriteLine($"'{value}' is not a valid C# type name for --{option}.");
                return Task.FromResult(1);
            }
        }

        var project = ProjectLocator.Locate(_fileSystem, _workingDirectory);
        if (project is null)
        {
            Console.Error.WriteLine($"Couldn't find a single .csproj at or above '{_workingDirectory}'. Run this inside a Rask project.");
            return Task.FromResult(1);
        }

        if (!TryBuild(kind, name, project, parsed, out var result, out var buildError))
        {
            Console.Error.WriteLine(buildError);
            return Task.FromResult(1);
        }

        return Task.FromResult(Write(result, parsed.HasFlag("force"), parsed.HasFlag("dry-run")));
    }

    private bool TryBuild(string kind, string name, ProjectContext project, ParsedArguments parsed, out ScaffoldResult result, out string? error)
    {
        error = null;

        switch (kind)
        {
            case "page":
                result = ScaffoldResult.Single(PageGenerator.Generate(project, _workingDirectory, name, parsed.Option("route"), parsed.Option("output")));
                return true;

            case "component":
                result = ScaffoldResult.Single(ComponentGenerator.Generate(project, _workingDirectory, name, parsed.Option("output")));
                return true;

            default: // feature
                var fieldsSpec = parsed.Option("fields");
                if (string.IsNullOrWhiteSpace(fieldsSpec))
                {
                    result = null!;
                    error = "'generate feature' needs --fields, e.g. --fields \"Name:string,Price:decimal\".";
                    return false;
                }

                if (!FieldSpecParser.TryParse(fieldsSpec, out var fields, out var fieldError))
                {
                    result = null!;
                    error = fieldError;
                    return false;
                }

                var collision = fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (collision is not null)
                {
                    result = null!;
                    error = $"Field '{collision.Name}' can't share the entity's name '{name}' (a member can't match its type).";
                    return false;
                }

                result = FeatureGenerator.Generate(project, _workingDirectory, name, fields, parsed.Option("context"), parsed.Option("plural"), parsed.Option("output"));
                return true;
        }
    }

    private int Write(ScaffoldResult result, bool force, bool dryRun)
    {
        if (!force)
        {
            var existing = result.Files
                .Where(f => _fileSystem.FileExists(f.Path))
                .Select(f => Display(f.Path))
                .ToArray();

            if (existing.Length > 0)
            {
                Console.Error.WriteLine($"Refusing to overwrite existing file(s): {string.Join(", ", existing)}. Pass --force.");
                return 1;
            }
        }

        if (dryRun)
        {
            foreach (var file in result.Files)
            {
                Console.Out.WriteLine($"[dry-run] would write {Display(file.Path)}:");
                Console.Out.WriteLine();
                Console.Out.WriteLine(file.Content);
            }

            WriteNotes(result.Notes);
            return 0;
        }

        foreach (var file in result.Files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            _fileSystem.WriteAllText(file.Path, file.Content);
            Console.Out.WriteLine($"Created {Display(file.Path)}");
        }

        WriteNotes(result.Notes);
        return 0;
    }

    private void WriteNotes(string? notes)
    {
        if (!string.IsNullOrEmpty(notes))
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(notes);
        }
    }

    private string Display(string path) => Path.GetRelativePath(_workingDirectory, path);
}
