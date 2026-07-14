using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask generate</c> — scaffold source into the current project. Locates the owning <c>.csproj</c>,
/// derives the folder-based namespace, and writes an idiomatic file for the requested artifact
/// (<c>page</c> or <c>component</c>), refusing to clobber an existing file unless <c>--force</c>.
/// </summary>
internal sealed class GenerateCommand(IConsole console, IFileSystem fileSystem, string workingDirectory)
    : CliCommand(console)
{
    private static readonly string[] Kinds = ["page", "component"];

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "generate";

    public override string Summary => "Scaffold a page or component into the current project.";

    public override string Usage =>
        "rask generate <page|component> <Name> [--route <path>] [--output <dir>] [--force] [--dry-run]";

    public override Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine($"Specify what to generate: {string.Join(" or ", Kinds)}.");
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
        if (kind == "component" && route is not null)
        {
            Console.Error.WriteLine("--route only applies to 'generate page'.");
            return Task.FromResult(1);
        }

        if (route is not null && !Identifiers.IsValidRoutePath(route))
        {
            Console.Error.WriteLine($"'{route}' is not a valid route path (no quotes, backslashes, or control characters).");
            return Task.FromResult(1);
        }

        var project = ProjectLocator.Locate(_fileSystem, _workingDirectory);
        if (project is null)
        {
            Console.Error.WriteLine($"Couldn't find a single .csproj at or above '{_workingDirectory}'. Run this inside a Rask project.");
            return Task.FromResult(1);
        }

        var file = kind switch
        {
            "page" => PageGenerator.Generate(project, _workingDirectory, name, route, parsed.Option("output")),
            _ => ComponentGenerator.Generate(project, _workingDirectory, name, parsed.Option("output")),
        };

        return Task.FromResult(Write(file, parsed.HasFlag("force"), parsed.HasFlag("dry-run")));
    }

    private int Write(ScaffoldFile file, bool force, bool dryRun)
    {
        var display = Path.GetRelativePath(_workingDirectory, file.Path);

        if (_fileSystem.FileExists(file.Path) && !force)
        {
            Console.Error.WriteLine($"{display} already exists. Pass --force to overwrite.");
            return 1;
        }

        if (dryRun)
        {
            Console.Out.WriteLine($"[dry-run] would write {display}:");
            Console.Out.WriteLine();
            Console.Out.WriteLine(file.Content);
            return 0;
        }

        var directory = Path.GetDirectoryName(file.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        _fileSystem.WriteAllText(file.Path, file.Content);
        Console.Out.WriteLine($"Created {display}");
        return 0;
    }
}
