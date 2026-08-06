using System.Text.RegularExpressions;
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
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "generate";

    public override IReadOnlyList<string> Aliases => ["g"];

    public override string Summary => "Scaffold a page, component, CRUD feature, background job, email, or cache into the current project.";

    // The shape only — the option list lives in the schema, which --help renders directly. Spelling the
    // flags out here is what let this string go stale before.
    public override string Usage => "rask generate <artifact> <Name> [<field:type> ...] [options]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
    [
        ("<Name>", "The type name, e.g. Product or Dashboard."),
        ("[<field:type> ...]", "Fields for a feature, e.g. Name:string Price:decimal."),
    ];

    public override IReadOnlyList<string> Examples =>
    [
        "rask generate page Dashboard --route /dashboard",
        "rask generate component UserCard",
        "rask generate component OrderRow --feature Orders",
        "rask generate feature Product Name:string Price:decimal",
        "rask generate feature Order Total:decimal --bs --modal --tests",
        "rask g j SendWelcomeEmail",
        "rask generate cache PopularProducts --feature Catalog",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private const string FeatureGroup = "Feature options (rask generate feature)";

    /// <summary>The flag/option schema — shared by <see cref="ExecuteAsync"/> and <c>--help</c> so they can't drift.</summary>
    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Verb("page", "A routed page component.", "p")
            .Verb("component", "A reusable component.", "c")
            .Verb("feature", "A full CRUD slice: entity, DbContext, commands, and pages.", "f")
            .Verb("job", "A background job handler.", "j")
            .Verb("email", "An email message and its send path.", "e")
            .Verb("cache", "A cached read accessor.", "ca")
            .Option("output", 'o', "dir", "Directory to write into (default: derived from the artifact).")
            // generate was the only command of the four without one, so when project resolution failed —
            // which it does whenever a directory holds more than one .csproj — the error had no escape
            // hatch to suggest (#601).
            .Option("project", 'p', "path", "Project to scaffold into (default: found from the current directory).")
            // Uppercase because '-f' is --fields, which this command also declares. The only uppercase
            // short in the CLI, and it keeps its reason after #601 rather than being an accident.
            .Option("feature", 'F', "Name", "Co-locate under Features/<Name>/ instead of Features/Shared/ (component, job, email).")
            .Flag("force", description: "Overwrite existing files instead of refusing.")
            .Flag("dry-run", description: "Print what would be written without touching disk.")
            .Option("route", 'r', "path", "URL route for the page.", group: "Page options")
            .Option("fields", 'f', "list", "Fields as Name:type,... (or pass them positionally).", FeatureGroup)
            .Option("context", 'c', "Name", "Reuse an existing DbContext instead of generating one.", FeatureGroup)
            // No short name: '-p' is --project CLI-wide. --plural held it only here, so `rask g f -p X`
            // meant something different from the same keystrokes on dev/db/deploy.
            .Option("plural", valueHint: "Name", description: "Plural name for the feature folder/route (default: auto-pluralized).", group: FeatureGroup)
            .Option("id", valueHint: "type", description: "Primary-key type (default: guid).", group: FeatureGroup, choices: ["guid", "int", "long"])
            .Option("validation", valueHint: "mode", description: "Validation style (default: valueobjects).", group: FeatureGroup, choices: ["valueobjects", "dataannotations", "fluent"])
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

    /// <summary>
    /// Set when a step we <em>attempted</em> failed — adding a package the generated code needs. The files
    /// are written and correct, but the project cannot build, so the command must not report success:
    /// before this, an offline `rask generate feature` exited 0 with its packages missing and a script or
    /// CI step carried straight on.
    ///
    /// <para>Deliberately not set when we <em>declined</em> to act — a Program.cs that isn't top-level
    /// statements gets its registrations printed instead of spliced. That's a documented fallback for a
    /// project shape we won't edit blind, not a failure, and treating it as one would make every generate
    /// in such a project exit non-zero.</para>
    /// </summary>
    private bool _wiringIncomplete;

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();
        if (!schema.TryResolveVerb(args.FirstOrDefault(), out var kind))
        {
            return FailUnknownVerb(args.FirstOrDefault(), schema);
        }

        var parsed = schema.Parse(args.Skip(1).ToArray());
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var name = parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Fail($"A name is required, e.g. 'rask generate {kind} Product'.");
        }

        if (!Identifiers.IsValidTypeName(name))
        {
            return Fail($"'{name}' is not a valid C# type name (letters, digits, and '_'; not starting with a digit).");
        }

        var route = parsed.Option("route");
        if (route is not null)
        {
            if (kind != "page")
            {
                return Fail("--route only applies to 'generate page'.");
            }

            if (!Identifiers.IsValidRoutePath(route))
            {
                return Fail($"'{route}' is not a valid route path (no quotes, backslashes, or control characters).");
            }
        }

        // --feature co-locates a component/job/email into a slice folder. It's meaningless for a page (its
        // slice comes from the class name) and for a feature (which *is* a slice), so reject it there.
        var feature = parsed.Option("feature");

        // --output names the folder outright; --feature asks for one to be chosen. Passing both looks like
        // it should co-locate inside the output folder, but --output simply wins and --feature is
        // discarded — so the file lands somewhere the user didn't ask for, under a namespace they didn't
        // expect. Reject the combination rather than silently pick one.
        if (feature is not null && parsed.Option("output") is not null)
        {
            return Fail("--output and --feature can't be combined — --output already says where the file goes.");
        }

        if (feature is not null && kind is "page" or "feature")
        {
            return Fail("--feature only applies to 'generate component', 'job', or 'email'.");
        }

        // Derived from the schema's own grouping rather than a hand-kept list, so a new feature option can't
        // be forgotten here (--no-restore was, and slipped through on a page for exactly that reason).
        // `--context` is the exception: `generate email` also accepts it (which context to wire the mail queue into).
        var misapplied = FeatureOnly(schema, parsed);
        if (kind == "email")
        {
            misapplied = misapplied.Where(o => o != "--context").ToList();
        }

        if (kind != "feature" && misapplied.Count > 0)
        {
            var verb = misapplied.Count == 1 ? "applies" : "apply";
            return Fail($"{Humanize(misapplied)} only {verb} to 'generate feature'.");
        }

        // Positional field specs (Name:type) are how 'generate feature' takes its fields; a page/component
        // has no fields, so extra positionals there are a mistake, not silently ignored.
        if (kind != "feature" && parsed.Positionals.Count > 1)
        {
            return Fail($"Unexpected argument '{parsed.Positionals[1]}'. Positional field specs (Name:type) only apply to 'generate feature'.");
        }

        foreach (var (option, value) in new[] { ("context", parsed.Option("context")), ("plural", parsed.Option("plural")), ("feature", feature) })
        {
            if (value is not null && !Identifiers.IsValidTypeName(value))
            {
                return Fail($"'{value}' is not a valid C# type name for --{option}.");
            }
        }

        // --project points at the .csproj or its directory; without it, search upward from where the
        // command was run. Resolving from the project's own directory rather than the cwd is what makes
        // it an escape hatch for a solution folder holding several projects.
        var searchFrom = _workingDirectory;
        if (parsed.Option("project") is { Length: > 0 } explicitProject)
        {
            var full = Path.GetFullPath(Path.Combine(_workingDirectory, explicitProject));
            searchFrom = full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(full) ?? _workingDirectory
                : full;
        }

        var project = ProjectLocator.Locate(_fileSystem, searchFrom);
        if (project is null)
        {
            Console.WriteErrorLine(
                $"{ProjectLocator.DescribeMissing(_fileSystem, searchFrom)} "
                + "Run this inside a Rask project, or point at one with --project <path>.",
                ConsoleStyle.Error);
            return 1;
        }

        // --output names a folder INSIDE the project: the namespace is derived from its path, so a folder
        // outside can't produce a coherent one. It used to be accepted — files were written outside the
        // project and quietly given the root namespace instead of failing.
        if (parsed.Option("output") is { } outputOverride)
        {
            var target = Scaffold.TargetDirectory(_workingDirectory, outputOverride);
            if (!Scaffold.IsInside(project.ProjectDirectory, target))
            {
                return Fail(
                [
                    $"--output '{outputOverride}' resolves outside the project ({target}).",
                    $"Generated code is namespaced by its folder, so it has to live under '{project.ProjectDirectory}'.",
                ]);
            }
        }

        // Everything TryBuild rejects is something in the argument list — a field spec that doesn't parse,
        // fields given twice, fields missing — so it exits like any other bad command line.
        if (!TryBuild(kind, name, project, parsed, out var result, out var buildError))
        {
            return Fail(buildError!);
        }

        // Whether a --tests run needs to wire a *new* test project (vs reuse one an earlier run created) must be
        // decided before Write, which is what creates the .csproj.
        var testProjectIsNew = result.TestProject is not null && !_fileSystem.FileExists(result.TestProject.ProjectPath);

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

            WireProgramCs(project.ProjectDirectory, result);
            EditContext(result);

            if (testProjectIsNew && result.TestProject is not null)
            {
                await WireTestProjectAsync(project, result.TestProject, parsed.HasFlag("no-restore"), cancellationToken).ConfigureAwait(false);
            }
        }

        if (written == 0)
        {
            WriteNotes(result.Notes);
        }

        if (written == 0 && _wiringIncomplete)
        {
            Console.Out.WriteLine();
            Console.WriteErrorLine(
                "The files were written, but the wiring above didn't complete — the project won't build until you finish it.",
                ConsoleStyle.Error);
            return 1;
        }

        return written;
    }

    // Insert the generator's service registrations into Program.cs so the output is runnable without a manual
    // paste. Idempotent: a statement (or using) already present is left alone, so a second feature adds only
    // what's new. If Program.cs isn't found or isn't top-level statements we can't safely edit, the same block
    // is printed as a manual fallback — the files are already written, so this is never fatal.
    private void WireProgramCs(string projectDirectory, ScaffoldResult result)
    {
        if (result.ProgramRegistrations.Count == 0)
        {
            return;
        }

        var path = Path.Combine(projectDirectory, "Program.cs");
        if (!_fileSystem.FileExists(path))
        {
            PrintManualRegistrations(result, "Couldn't find Program.cs — register these services yourself:");
            return;
        }

        var text = _fileSystem.ReadAllText(path);
        if (!text.Contains("WebApplication.CreateBuilder", StringComparison.Ordinal))
        {
            PrintManualRegistrations(result, "Program.cs isn't top-level statements — register these services yourself:");
            return;
        }

        var (updated, added) = SpliceProgramCs(text, result.ProgramUsings, result.ProgramRegistrations);
        if (updated == text)
        {
            return; // everything was already wired
        }

        _fileSystem.WriteAllText(path, updated);
        var names = string.Join(", ", added.Select(RegistrationName));
        Console.WriteLine($"Registered {added.Count} service(s) in Program.cs: {names}.", ConsoleStyle.Success);
    }

    /// <summary>
    /// Pure splice: insert any missing <paramref name="usings"/> (after the last using) and
    /// <paramref name="registrations"/> (after the last <c>builder.Services.</c> line) into a top-level-statements
    /// <paramref name="text"/>, idempotently — a directive or registration already present is left alone. Returns
    /// the rewritten text (the original instance when nothing changed) and the registration first-lines added.
    /// Extracted so the exact splice can be unit-tested and compile-gated. Caller guards for
    /// <c>WebApplication.CreateBuilder</c> before calling.
    /// </summary>
    internal static (string Text, IReadOnlyList<string> Added) SpliceProgramCs(
        string text, IReadOnlyList<string> usings, IReadOnlyList<string> registrations)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        // Add missing usings after the last existing using directive (or at the very top).
        var addedUsings = 0;
        var lastUsing = lines.FindLastIndex(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal) && l.TrimEnd().EndsWith(';'));
        var usingAt = lastUsing >= 0 ? lastUsing + 1 : 0;
        foreach (var ns in usings)
        {
            var directive = $"using {ns};";
            if (!lines.Any(l => l.Trim() == directive))
            {
                lines.Insert(usingAt++, directive);
                addedUsings++;
            }
        }

        // Insert registrations after the last existing builder.Services line, else right after the builder.
        var added = new List<string>();
        foreach (var registration in registrations)
        {
            var firstLine = registration.Split('\n')[0];

            // Match on the extension method, not the exact text: `rask new --outbox` already emits
            // `builder.Services.AddRaskData(o => { … });` over several lines, so comparing whole lines would
            // miss it and append a second AddRaskData. That is worse than untidy — AddRaskData is guarded so
            // that the FIRST call wins, meaning a later call's options are silently dropped.
            var method = RegistrationName(firstLine);
            var existing = lines.FindIndex(l =>
                l.TrimStart().StartsWith("builder.Services.", StringComparison.Ordinal) &&
                RegistrationName(l) == method);

            if (existing >= 0)
            {
                // One exception to "leave what's there": turning the outbox on has to turn the in-process
                // domain-event publisher off, or DomainEventInterceptor drains and clears each entity's
                // events before OutboxInterceptor can copy them — the outbox table stays empty and delivery
                // quietly stops being durable while every handler still runs. Upgrade the bare call rather
                // than leaving the user with a broken outbox they have no way to notice. Only the bare call
                // is rewritten: anyone who has already customised AddRaskData gets to keep their version
                // (and `rask new --outbox` emits the disabling form to begin with).
                if (registration.Contains("DispatchDomainEventsInProcess = false", StringComparison.Ordinal) &&
                    !lines[existing].Contains("DispatchDomainEventsInProcess", StringComparison.Ordinal) &&
                    lines[existing].Trim() == "builder.Services.AddRaskData();")
                {
                    lines[existing] = firstLine;
                    added.Add(firstLine);
                }

                continue; // already registered (e.g. a second feature re-adding AddRaskCqrs)
            }

            var anchor = lines.FindLastIndex(l => l.TrimStart().StartsWith("builder.Services.", StringComparison.Ordinal));
            if (anchor < 0)
            {
                anchor = lines.FindLastIndex(l => l.Contains("WebApplication.CreateBuilder", StringComparison.Ordinal));
            }

            // A registration can span multiple lines (e.g. `AddDbContextFactory<T>((sp, o) => o` with a fluent
            // body on the lines below); the match above finds where that statement *starts*. Advance to the line
            // that ends it (terminating in ';') so the new registration lands after the whole statement, not
            // spliced into the middle of it — which would produce invalid C#.
            if (anchor >= 0)
            {
                while (anchor < lines.Count - 1 && !lines[anchor].TrimEnd().EndsWith(';'))
                {
                    anchor++;
                }
            }

            lines.InsertRange(anchor + 1, registration.Split('\n'));
            added.Add(firstLine);
        }

        return added.Count == 0 && addedUsings == 0 ? (text, added) : (string.Join(newline, lines), added);
    }

    // Find an existing DbContext class by name anywhere in the project, returning its namespace + file so an
    // --context run compiles (the slice imports it) and its DbSet can be added. (null, null) if not found.
    private (string? Namespace, string? FilePath) ResolveContext(ProjectContext project, string contextName)
    {
        var declaration = new Regex($@"\b(?:class|record)\s+{Regex.Escape(contextName)}\b");
        var namespaceOf = new Regex(@"\bnamespace\s+([\w.]+)");
        foreach (var file in _fileSystem.ListFilesRecursive(project.ProjectDirectory, "*.cs"))
        {
            var text = _fileSystem.ReadAllText(file);
            if (!declaration.IsMatch(text))
            {
                continue;
            }

            var match = namespaceOf.Match(text);
            return (match.Success ? match.Groups[1].Value : project.RootNamespace, file);
        }

        return (null, null);
    }

    // Every class that directly extends DbContext in the project (name + namespace + file). `generate email`
    // auto-detects the one to wire the mail queue into.
    private IReadOnlyList<(string Name, string Namespace, string FilePath)> FindContexts(ProjectContext project)
    {
        var declaration = new Regex(@"\bclass\s+(\w+)\b[^{;]*:\s*DbContext\b");
        var namespaceOf = new Regex(@"\bnamespace\s+([\w.]+)");
        var found = new List<(string, string, string)>();
        foreach (var file in _fileSystem.ListFilesRecursive(project.ProjectDirectory, "*.cs"))
        {
            var text = _fileSystem.ReadAllText(file);
            var match = declaration.Match(text);
            if (!match.Success)
            {
                continue;
            }

            var ns = namespaceOf.Match(text);
            found.Add((match.Groups[1].Value, ns.Success ? ns.Groups[1].Value : project.RootNamespace, file));
        }

        return found;
    }

    // The DbContext `generate email` wires the mail queue into: an explicit --context (if found), else the one
    // context in the project. null when it can't be resolved unambiguously (no context, or several with no
    // --context) — the caller falls back to printing the manual wiring steps.
    private (string Name, string Namespace, string FilePath)? ResolveMailContext(ProjectContext project, string? explicitName)
    {
        if (explicitName is not null)
        {
            var (ns, path) = ResolveContext(project, explicitName);
            return ns is not null && path is not null ? (explicitName, ns, path) : null;
        }

        var found = FindContexts(project);
        return found.Count == 1 ? found[0] : null;
    }

    // Edit the target DbContext in place: add the --context run's DbSet properties + usings (so EF maps the new
    // entities) and/or OnModelCreating statements (e.g. modelBuilder.AddRaskMail() for `generate email`). All
    // idempotent — a set/using/line already present is left alone. When the context file couldn't be located,
    // the same instructions are printed as a fallback so nothing is silently dropped.
    private void EditContext(ScaffoldResult result)
    {
        if (result.ContextDbSets.Count == 0 && result.ContextModelLines.Count == 0)
        {
            return;
        }

        if (result.ContextFilePath is null || !_fileSystem.FileExists(result.ContextFilePath))
        {
            // The context class wasn't found in the project — tell the user what to add to it themselves.
            if (result.ContextDbSets.Count > 0)
            {
                Console.WriteLine("Add these to your DbContext:", ConsoleStyle.Dim);
                foreach (var set in result.ContextDbSets)
                {
                    Console.WriteLine(set, ConsoleStyle.Dim);
                }
            }

            if (result.ContextModelLines.Count > 0)
            {
                Console.WriteLine("Add these to your DbContext's OnModelCreating:", ConsoleStyle.Dim);
                foreach (var line in result.ContextModelLines)
                {
                    Console.WriteLine(line, ConsoleStyle.Dim);
                }
            }

            return;
        }

        var text = _fileSystem.ReadAllText(result.ContextFilePath);
        var (updated, addedSets, addedModel, unplacedModelLines) =
            SpliceContext(text, result.ContextUsings, result.ContextDbSets, result.ContextModelLines);

        // A custom context with no ApplyRaskConventions anchor: we can't place the OnModelCreating lines
        // safely, so print them for the user rather than guessing a location.
        foreach (var line in unplacedModelLines)
        {
            Console.WriteLine($"Add to your DbContext's OnModelCreating: {line.Trim()}", ConsoleStyle.Dim);
        }

        if (ReferenceEquals(updated, text))
        {
            return; // everything was already present (or only unplaced lines, surfaced above)
        }

        _fileSystem.WriteAllText(result.ContextFilePath, updated);
        if (addedSets > 0)
        {
            Console.WriteLine($"Added {addedSets} DbSet(s) to {Display(result.ContextFilePath)}.", ConsoleStyle.Success);
        }

        if (addedModel > 0)
        {
            Console.WriteLine($"Mapped {addedModel} table(s) in {Display(result.ContextFilePath)} (OnModelCreating).", ConsoleStyle.Success);
        }
    }

    /// <summary>
    /// Pure splice for a DbContext file: insert any missing <paramref name="usings"/> (after the last using),
    /// <paramref name="dbSets"/> (after the last <c>public DbSet&lt;</c>, else after the class-body brace), and
    /// <paramref name="modelLines"/> (inside <c>OnModelCreating</c>, after the <c>ApplyRaskConventions</c> call) —
    /// all idempotently, a member already present is left alone. Returns the rewritten text (the original
    /// instance when nothing changed), the counts added, and any model lines that had no anchor to attach to (a
    /// custom context without <c>ApplyRaskConventions</c>) so the caller can surface them. Extracted from
    /// <see cref="EditContext"/> — mirroring <see cref="SpliceProgramCs"/> — so the exact splice can be
    /// unit-tested and compile-gated.
    /// </summary>
    internal static (string Text, int AddedSets, int AddedModel, IReadOnlyList<string> UnplacedModelLines) SpliceContext(
        string text, IReadOnlyList<string> usings, IReadOnlyList<string> dbSets, IReadOnlyList<string> modelLines)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();

        var addedUsings = 0;
        var lastUsing = lines.FindLastIndex(l => l.TrimStart().StartsWith("using ", StringComparison.Ordinal) && l.TrimEnd().EndsWith(';'));
        var usingAt = lastUsing >= 0 ? lastUsing + 1 : 0;
        foreach (var ns in usings)
        {
            var directive = $"using {ns};";
            if (!lines.Any(l => l.Trim() == directive))
            {
                lines.Insert(usingAt++, directive);
                addedUsings++;
            }
        }

        var addedSets = 0;
        foreach (var set in dbSets)
        {
            if (lines.Any(l => l.Trim() == set.Trim()))
            {
                continue;
            }

            // After the last existing DbSet property (the generated context always has at least one), else after
            // the class body's opening brace.
            var anchor = lines.FindLastIndex(l => l.TrimStart().StartsWith("public DbSet<", StringComparison.Ordinal));
            if (anchor < 0)
            {
                anchor = lines.FindIndex(l => l.TrimEnd().EndsWith('{'));
            }

            if (anchor < 0)
            {
                continue;
            }

            lines.Insert(anchor + 1, set);
            addedSets++;
        }

        var addedModel = 0;
        var unplaced = new List<string>();
        foreach (var line in modelLines)
        {
            if (lines.Any(l => l.Trim() == line.Trim()))
            {
                continue;
            }

            // Inside OnModelCreating, after the Rask conventions call the generated contexts always make. If a
            // custom context has no such call we can't place it safely — hand it back for the caller to surface.
            var anchor = lines.FindLastIndex(l => l.Contains("ApplyRaskConventions", StringComparison.Ordinal));
            if (anchor < 0)
            {
                unplaced.Add(line);
                continue;
            }

            lines.Insert(anchor + 1, line);
            addedModel++;
        }

        return addedSets == 0 && addedUsings == 0 && addedModel == 0
            ? (text, 0, 0, unplaced)
            : (string.Join(newline, lines), addedSets, addedModel, unplaced);
    }

    // A short display name for a registration line, e.g. "builder.Services.AddRaskCqrs();" -> "AddRaskCqrs".
    private static string RegistrationName(string firstLine)
    {
        var call = firstLine.Trim();
        var open = call.IndexOf('(');
        var dot = open > 0 ? call.LastIndexOf('.', open) : call.LastIndexOf('.');
        return dot >= 0 && open > dot ? call[(dot + 1)..open] : call;
    }

    private void PrintManualRegistrations(ScaffoldResult result, string heading)
    {

        Console.WriteLine(heading, ConsoleStyle.Dim);
        foreach (var ns in result.ProgramUsings)
        {
            Console.WriteLine($"  using {ns};", ConsoleStyle.Dim);
        }

        foreach (var registration in result.ProgramRegistrations)
        {
            foreach (var line in registration.Split('\n'))
            {
                Console.WriteLine("  " + line, ConsoleStyle.Dim);
            }
        }
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

        // Pin the Rask.* packages to the CLI's own version so a generated feature can't float them past the
        // Rask.Server the template baked (a locally/CI-built CLI would otherwise mix e.g. Rask.Server 0.17.0
        // with a newer Rask.Data pulled from nuget.org). Non-Rask packages (EF Core, SQLitePCLRaw) keep
        // floating — they version independently of Rask and resolve to the latest compatible.
        var raskVersion = NewCommand.ResolvePackageVersion(CliMetadata.Version);

        Console.WriteLine($"Adding {packages.Count} package(s) to the project…", ConsoleStyle.Dim);
        foreach (var package in packages)
        {
            var isRask = package.StartsWith("Rask.", StringComparison.Ordinal);
            string[] args = isRask
                ? ["add", "package", package, "--version", raskVersion]
                : ["add", "package", package];
            var exit = await _process.RunAsync("dotnet", args, projectDirectory, cancellationToken).ConfigureAwait(false);
            if (exit != 0)
            {
                var manual = isRask ? $"dotnet add package {package} --version {raskVersion}" : $"dotnet add package {package}";
                _wiringIncomplete = true;
                WriteWarning($"  Couldn't add {package} automatically — add it manually: {manual}");
            }
        }
    }

    // Wire a freshly-created <Project>.Tests project: reference the app, add the test SDK + xUnit packages, and
    // register it in the solution if there is one. Failures are warnings — the files are written regardless.
    private async Task WireTestProjectAsync(ProjectContext project, TestProjectWiring test, bool noRestore, CancellationToken cancellationToken)
    {
        var appCsproj = _fileSystem.ListFiles(project.ProjectDirectory, "*.csproj").FirstOrDefault();
        if (appCsproj is null)
        {
            return;
        }

        if (noRestore)
        {
            Console.WriteLine($"Skipped wiring {Display(test.ProjectPath)} (--no-restore): reference {Display(appCsproj)} + add {string.Join(", ", test.Packages)}, then dotnet restore.", ConsoleStyle.Dim);
            return;
        }

        Console.WriteLine($"Wiring the test project ({Display(test.ProjectPath)})…", ConsoleStyle.Dim);
        await RunDotnetAsync(["add", test.ProjectPath, "reference", appCsproj], project.ProjectDirectory, cancellationToken).ConfigureAwait(false);
        foreach (var package in test.Packages)
        {
            await RunDotnetAsync(["add", test.ProjectPath, "package", package], project.ProjectDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (FindSolution(project.ProjectDirectory) is { } solution)
        {
            await RunDotnetAsync(["sln", solution, "add", test.ProjectPath], project.ProjectDirectory, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunDotnetAsync(IReadOnlyList<string> args, string workingDirectory, CancellationToken cancellationToken)
    {
        var exit = await _process.RunAsync("dotnet", args, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            WriteWarning($"  `dotnet {string.Join(' ', args)}` failed — run it yourself if the test project needs it.");
        }
    }

    // The nearest .sln/.slnx at or above the project, so a generated test project joins the same solution.
    private string? FindSolution(string startDirectory)
    {
        var dir = startDirectory;
        for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(dir); depth++)
        {
            if ((_fileSystem.ListFiles(dir, "*.sln").FirstOrDefault() ?? _fileSystem.ListFiles(dir, "*.slnx").FirstOrDefault()) is { } solution)
            {
                return solution;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        return null;
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
                result = ScaffoldResult.Single(ComponentGenerator.Generate(project, _workingDirectory, name, parsed.Option("feature"), parsed.Option("output")));
                return true;

            case "job":
                result = JobGenerator.Generate(project, _workingDirectory, name, parsed.Option("feature"), parsed.Option("output"));
                return true;

            case "email":
                result = EmailGenerator.Generate(project, _workingDirectory, name, parsed.Option("feature"), parsed.Option("output"), ResolveMailContext(project, parsed.Option("context")));
                return true;

            case "cache":
                result = CacheGenerator.Generate(project, _workingDirectory, name, parsed.Option("feature"), parsed.Option("output"));
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
                // Locate an --context DbContext so the slice can import its namespace and the DbSet can be added
                // to it. Best-effort: a class we can't find leaves the generator's pre-fix behaviour intact.
                var contextOverride = parsed.Option("context");
                var (contextNamespace, contextFilePath) = contextOverride is null
                    ? (null, null)
                    : ResolveContext(project, contextOverride);

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
                    ContextOverride = contextOverride,
                    ContextNamespace = contextNamespace,
                    OutputOverride = parsed.Option("output"),
                };

                result = FeatureGenerator.Generate(project, _workingDirectory, spec, options);
                if (contextFilePath is not null)
                {
                    result = result with { ContextFilePath = contextFilePath };
                }

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
                Console.WriteErrorLine(
                    $"Refusing to overwrite existing file(s): {string.Join(", ", existing)}. "
                    + "Pass --force to replace them, or --dry-run to see what would be written.",
                    ConsoleStyle.Error);
                return 1;
            }
        }

        if (dryRun)
        {
            foreach (var file in result.Files.Concat(result.CreateIfAbsent.Where(f => !_fileSystem.FileExists(f.Path))))
            {
                Console.Out.WriteLine($"[dry-run] would write {Display(file.Path)}:");
                Console.Out.WriteLine();
                Console.Out.WriteLine(file.Content);
            }

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

        // Create-if-absent files (e.g. the test project's .csproj) are written only when missing — never
        // overwritten, so a second --tests run reuses the project the first one created.
        foreach (var file in result.CreateIfAbsent)
        {
            if (_fileSystem.FileExists(file.Path))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            _fileSystem.WriteAllText(file.Path, file.Content);
            WriteCreated(Display(file.Path));
        }

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
