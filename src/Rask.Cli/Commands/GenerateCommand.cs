using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask generate</c> — scaffold source into the current project. Locates the owning <c>.csproj</c>,
/// derives the folder-based namespace, and writes idiomatic files for the requested artifact
/// (<c>page</c>, <c>component</c>, or a full CRUD <c>feature</c>), refusing to clobber an existing file
/// unless <c>--force</c>.
/// </summary>
internal sealed class GenerateCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private static readonly string[] Kinds = ["page", "component", "feature", "job", "email"];

    private static readonly Dictionary<string, string> KindAliases = new(StringComparer.Ordinal)
    {
        ["p"] = "page",
        ["c"] = "component",
        ["f"] = "feature",
        ["j"] = "job",
        ["e"] = "email",
    };

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "generate";

    public override IReadOnlyList<string> Aliases => ["g"];

    public override string Summary => "Scaffold a page, component, CRUD feature, background job, or email into the current project.";

    // The shape only — the option list lives in the schema, which --help renders directly. Spelling the
    // flags out here is what let this string go stale before.
    public override string Usage =>
        "rask generate <page|component|feature|job|email> <Name> [<field:type> ...] [options]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
    [
        ("<page|component|feature|job|email>", "What to scaffold (aliases: p, c, f, j, e)."),
        ("<Name>", "The type name, e.g. Product or Dashboard."),
        ("[<field:type> ...]", "Fields for a feature, e.g. Name:string Price:decimal."),
    ];

    public override IReadOnlyList<string> Examples =>
    [
        "rask generate page Dashboard --route /dashboard",
        "rask generate component UserCard",
        "rask generate feature Product Name:string Price:decimal",
        "rask generate feature Order Total:decimal --bs --modal --tests",
        "rask g j SendWelcomeEmail",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private const string FeatureGroup = "Feature options (rask generate feature)";

    /// <summary>The flag/option schema — shared by <see cref="ExecuteAsync"/> and <c>--help</c> so they can't drift.</summary>
    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Option("output", 'o', "dir", "Directory to write into (default: derived from the artifact).")
            .Flag("force", description: "Overwrite existing files instead of refusing.")
            .Flag("dry-run", description: "Print what would be written without touching disk.")
            .Option("route", 'r', "path", "URL route for the page.", group: "Page options")
            .Option("fields", 'f', "list", "Fields as Name:type,... (or pass them positionally).", FeatureGroup)
            .Option("context", 'c', "Name", "Reuse an existing DbContext instead of generating one.", FeatureGroup)
            .Option("plural", 'p', "Name", "Plural name for the feature folder/route (default: auto-pluralized).", FeatureGroup)
            .Option("id", valueHint: "guid|int|long", description: "Primary-key type (default: guid).", group: FeatureGroup)
            .Option("validation", valueHint: "mode", description: "Validation style: valueobjects (default), dataannotations, or fluent.", group: FeatureGroup)
            .Flag("bs", description: "Render pages with Rask.Bootstrap (Bs*) components.", group: FeatureGroup)
            .Flag("modal", description: "Fold create/edit into a modal on the list page (implies --bs).", group: FeatureGroup)
            .Flag("soft-delete", description: "Soft-delete rows and add a Restore command.", group: FeatureGroup)
            .Flag("concurrency", description: "Add optimistic-concurrency (row version) handling.", group: FeatureGroup)
            .Flag("events", description: "Raise domain events on create/update/delete.", group: FeatureGroup)
            .Flag("outbox", description: "Persist domain events via the transactional outbox (implies --events).", group: FeatureGroup)
            .Flag("tests", description: "Generate a sibling <Project>.Tests project with domain + persistence tests.", group: FeatureGroup)
            .Flag("no-restore", description: "Write files without adding packages or restoring.", group: FeatureGroup)
            .Flag("save-defaults", description: "Remember this run's feature flags in .rask/generate.json for next time.", group: FeatureGroup);

    /// <summary>The feature-only options this run actually supplied, in declaration order.</summary>
    private static List<string> FeatureOnly(ArgumentSchema schema, ParsedArguments parsed) =>
        schema.Declared
            .Where(o => o.Group == FeatureGroup)
            .Where(o => o.IsFlag ? parsed.HasFlag(o.LongName) : parsed.Option(o.LongName) is not null)
            .Select(o => "--" + o.LongName)
            .ToList();

    /// <summary>"--a" / "--a and --b" / "--a, --b, and --c".</summary>
    private static string Humanize(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
    };

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine($"Specify what to generate: {string.Join(", ", Kinds)}.");
            Console.Error.WriteLine($"Usage: {Usage}");
            return 1;
        }

        var kind = KindAliases.GetValueOrDefault(args[0], args[0]);
        if (!Kinds.Contains(kind))
        {
            Console.Error.WriteLine($"Unknown artifact '{args[0]}'. Generate one of: {string.Join(", ", Kinds)} (aliases: p, c, f, j, e).");
            return 1;
        }

        var schema = CreateSchema();

        var parsed = schema.Parse(args.Skip(1).ToArray());
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var name = parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine($"A name is required. Usage: {Usage}");
            return 1;
        }

        if (!Identifiers.IsValidTypeName(name))
        {
            Console.Error.WriteLine($"'{name}' is not a valid C# type name (letters, digits, and '_'; not starting with a digit).");
            return 1;
        }

        var route = parsed.Option("route");
        if (route is not null)
        {
            if (kind != "page")
            {
                Console.Error.WriteLine("--route only applies to 'generate page'.");
                return 1;
            }

            if (!Identifiers.IsValidRoutePath(route))
            {
                Console.Error.WriteLine($"'{route}' is not a valid route path (no quotes, backslashes, or control characters).");
                return 1;
            }
        }

        // Derived from the schema's own grouping rather than a hand-kept list, so a new feature option can't
        // be forgotten here (--no-restore was, and slipped through on a page for exactly that reason).
        if (kind != "feature" && FeatureOnly(schema, parsed) is { Count: > 0 } misapplied)
        {
            var verb = misapplied.Count == 1 ? "applies" : "apply";
            Console.Error.WriteLine($"{Humanize(misapplied)} only {verb} to 'generate feature'.");
            return 1;
        }

        // Positional field specs (Name:type) are how 'generate feature' takes its fields; a page/component
        // has no fields, so extra positionals there are a mistake, not silently ignored.
        if (kind != "feature" && parsed.Positionals.Count > 1)
        {
            Console.Error.WriteLine($"Unexpected argument '{parsed.Positionals[1]}'. Positional field specs (Name:type) only apply to 'generate feature'.");
            return 1;
        }

        foreach (var (option, value) in new[] { ("context", parsed.Option("context")), ("plural", parsed.Option("plural")) })
        {
            if (value is not null && !Identifiers.IsValidTypeName(value))
            {
                Console.Error.WriteLine($"'{value}' is not a valid C# type name for --{option}.");
                return 1;
            }
        }

        var project = ProjectLocator.Locate(_fileSystem, _workingDirectory);
        if (project is null)
        {
            Console.Error.WriteLine($"Couldn't find a single .csproj at or above '{_workingDirectory}'. Run this inside a Rask project.");
            return 1;
        }

        if (!TryBuild(kind, name, project, parsed, out var result, out var buildError))
        {
            Console.Error.WriteLine(buildError);
            return 1;
        }

        var dryRun = parsed.HasFlag("dry-run");
        var written = Write(result, parsed.HasFlag("force"), dryRun);
        if (written == 0 && !dryRun)
        {
            if (parsed.HasFlag("save-defaults"))
            {
                SaveDefaults(project.ProjectDirectory, parsed);
            }

            if (result.Packages.Count > 0)
            {
                await AddPackagesAsync(project.ProjectDirectory, result.Packages, parsed.HasFlag("no-restore"), cancellationToken).ConfigureAwait(false);
            }
        }

        return written;
    }

    // Add the packages the generated code needs straight into the project (dotnet add package). A failure
    // (e.g. offline, or the package isn't on a configured feed yet) is a warning, not a hard error — the
    // files are already written and the printed next-steps list the packages as a manual fallback.
    private async Task AddPackagesAsync(string projectDirectory, IReadOnlyList<string> packages, bool noRestore, CancellationToken cancellationToken)
    {
        if (noRestore)
        {
            Console.WriteLine($"Skipped adding packages (--no-restore): {string.Join(", ", packages)}", ConsoleStyle.Dim);
            return;
        }

        Console.WriteLine($"Adding {packages.Count} package(s) to the project…", ConsoleStyle.Dim);
        foreach (var package in packages)
        {
            var exit = await _process.RunAsync("dotnet", ["add", "package", package], projectDirectory, cancellationToken).ConfigureAwait(false);
            if (exit != 0)
            {
                WriteWarning($"  Couldn't add {package} automatically — add it manually: dotnet add package {package}");
            }
        }
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

            case "job":
                result = JobGenerator.Generate(project, _workingDirectory, name, parsed.Option("output"));
                return true;

            case "email":
                result = EmailGenerator.Generate(project, _workingDirectory, name, parsed.Option("output"));
                return true;

            default: // feature
                // Fields and relationships are positional: `generate feature Post Title:string 1:n Comment Body:text`.
                // The legacy `--fields "Title:string,1:n,Comment,Body:text"` form still works, but not both at once.
                var positionalTokens = parsed.Positionals.Skip(1).ToArray();
                var fieldsOption = parsed.Option("fields");
                if (positionalTokens.Length > 0 && fieldsOption is not null)
                {
                    result = null!;
                    error = "Specify fields positionally (e.g. Name:string Price:decimal) or with --fields, not both.";
                    return false;
                }

                // --fields is the same token stream, comma-separated — so relationships work in both forms.
                var tokens = positionalTokens.Length > 0
                    ? positionalTokens
                    : (fieldsOption ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (tokens.Length == 0)
                {
                    result = null!;
                    error = "'generate feature' needs fields, e.g. rask generate feature Product Name:string Price:decimal.";
                    return false;
                }

                if (!FeatureSpecParser.TryParse(name, parsed.Option("plural"), tokens, out var spec, out var specError))
                {
                    result = null!;
                    error = specError;
                    return false;
                }

                // The grammar parses and validates ahead of the emitter that will consume it. Refuse rather
                // than generate the root and drop the targets on the floor — silently discarding what was
                // asked for is worse than not supporting it yet.
                if (spec.Relationships.Count > 0)
                {
                    var relationship = spec.Relationships[0];
                    result = null!;
                    error = $"Relationships aren't generated yet — '{relationship.Token} {relationship.To.Name}' parses, but emitting it isn't implemented. Scaffold the entities separately for now.";
                    return false;
                }

                // Team defaults from .rask/generate.json fill in what wasn't passed; explicit flags always win.
                var config = GenerateConfig.Load(_fileSystem, project.ProjectDirectory);

                var idType = (parsed.Option("id") ?? config.Id)?.ToLowerInvariant() switch
                {
                    null or "guid" => "Guid",
                    "int" => "int",
                    "long" => "long",
                    _ => "",
                };

                if (idType.Length == 0)
                {
                    result = null!;
                    error = "--id must be 'guid' (default), 'int', or 'long'.";
                    return false;
                }

                var validation = (parsed.Option("validation") ?? config.Validation)?.ToLowerInvariant() ?? "valueobjects";
                if (validation is not ("valueobjects" or "dataannotations" or "fluent"))
                {
                    result = null!;
                    error = "--validation must be 'valueobjects' (default), 'dataannotations', or 'fluent'.";
                    return false;
                }

                // A flag set here or defaulted on in .rask/generate.json turns the option on; explicit wins.
                bool Flag(string longName, bool? configured) => parsed.HasFlag(longName) || (configured ?? false);

                // --modal puts create/update in a BsModal on the list page, so it implies --bs.
                var useModal = Flag("modal", config.Modal);
                var options = new FeatureOptions
                {
                    IdType = idType,
                    Validation = validation,
                    UseModal = useModal,
                    UseBs = useModal || Flag("bs", config.Bs),
                    UseSoftDelete = Flag("soft-delete", config.SoftDelete),
                    UseConcurrency = Flag("concurrency", config.Concurrency),
                    UseEvents = Flag("events", config.Events),
                    UseOutbox = Flag("outbox", config.Outbox),
                    UseTests = Flag("tests", config.Tests),
                    ContextOverride = parsed.Option("context"),
                    OutputOverride = parsed.Option("output"),
                };

                result = FeatureGenerator.Generate(project, _workingDirectory, spec, options);
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
            WriteCreated(Display(file.Path));
        }

        WriteNotes(result.Notes);
        return 0;
    }

    // Overlay this run's explicit feature flags/options onto any existing .rask/generate.json and save it —
    // so the next `rask generate feature` inherits them. Only sets values the user actually passed (booleans
    // are never written false), keeping the file to the team's deliberate choices.
    private void SaveDefaults(string projectDirectory, ParsedArguments parsed)
    {
        var config = GenerateConfig.Load(_fileSystem, projectDirectory);
        if (parsed.HasFlag("bs"))
        {
            config.Bs = true;
        }

        if (parsed.HasFlag("modal"))
        {
            config.Modal = true;
        }

        if (parsed.HasFlag("soft-delete"))
        {
            config.SoftDelete = true;
        }

        if (parsed.HasFlag("concurrency"))
        {
            config.Concurrency = true;
        }

        if (parsed.HasFlag("events"))
        {
            config.Events = true;
        }

        if (parsed.HasFlag("outbox"))
        {
            config.Outbox = true;
        }

        if (parsed.HasFlag("tests"))
        {
            config.Tests = true;
        }

        if (parsed.Option("validation") is { } validation)
        {
            config.Validation = validation;
        }

        if (parsed.Option("id") is { } id)
        {
            config.Id = id;
        }

        config.Save(_fileSystem, projectDirectory);
        Console.WriteLine($"Saved generate defaults to {Display(GenerateConfig.PathFor(projectDirectory))}.", ConsoleStyle.Success);
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
