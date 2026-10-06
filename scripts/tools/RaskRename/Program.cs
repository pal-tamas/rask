// Renames one symbol across Rask.slnx, then looks where no project reaches.
//
//   dotnet run --project scripts/tools/RaskRename -- Rask.Wasm.WasmHostBuilder.RunAsync Run [--dry-run]
//
// The first argument is the member as documentation spells it: namespace, type, member. An interface
// member takes its implementations with it; overloads go together.

using System.Text.RegularExpressions;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Rename;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: RaskRename <Namespace.Type.Member> <NewName> [--dry-run]");
    return 2;
}

var (qualified, newName, dryRun) = (args[0], args[1], args.Contains("--dry-run"));
var split = qualified.LastIndexOf('.');
var (typeName, oldName) = (qualified[..split], qualified[(split + 1)..]);
var root = FindRoot();

MSBuildLocator.RegisterDefaults();
using var workspace = MSBuildWorkspace.Create();
workspace.RegisterWorkspaceFailedHandler(_ => { });   // design-time noise; a symbol that cannot be found says so below
Console.WriteLine("Loading Rask.slnx ...");
var solution = await workspace.OpenSolutionAsync(Path.Combine(root, "Rask.slnx"));

var symbol = await Find(solution, typeName, oldName);
if (symbol is null)
{
    Console.Error.WriteLine($"No member '{oldName}' is declared in source on '{typeName}'.");
    return 1;
}

// The bare name may already be taken — a generated member, a sync twin. Renaming onto it would compile
// into something else or not at all, and which new word to use instead is a person's decision.
var taken = symbol.ContainingType.GetMembers(newName).FirstOrDefault();
if (taken is not null)
{
    Console.Error.WriteLine($"'{typeName}' already has a member named '{newName}': {taken.ToDisplayString()}. Nothing was changed.");
    return 1;
}

var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(RenameOverloads: true), newName);

var written = 0;
var conflicts = 0;
var seen = new HashSet<string>(StringComparer.Ordinal);   // a multi-targeted project holds each file once per framework
foreach (var id in renamed.GetChanges(solution).GetProjectChanges().SelectMany(p => p.GetChangedDocuments()))
{
    var document = renamed.GetDocument(id)!;
    if (document.FilePath is null || IsBuildOutput(document.FilePath) || !seen.Add(document.FilePath))
    {
        continue;
    }

    var syntax = await document.GetSyntaxRootAsync();
    conflicts += syntax!.GetAnnotatedNodesAndTokens(ConflictAnnotation.Kind).Count();
    if (!dryRun)
    {
        await File.WriteAllTextAsync(document.FilePath, (await document.GetTextAsync()).ToString());
    }

    written++;
    Console.WriteLine($"  {(dryRun ? "would change" : "changed")} {Path.GetRelativePath(root, document.FilePath)}");
}

Console.WriteLine($"{written} file(s) {(dryRun ? "would change" : "changed")} by the rename.");
if (conflicts > 0)
{
    Console.Error.WriteLine($"{conflicts} place(s) no longer bind to the same thing after the rename — build and read them.");
}

SweepUncompiledText(root, typeName[(typeName.LastIndexOf('.') + 1)..], oldName, newName, dryRun);
Console.WriteLine("Next: build (the PublicAPI baselines want the new name), then the docs and the CHANGELOG.");
return conflicts > 0 ? 1 : 0;

static async Task<ISymbol?> Find(Solution solution, string typeName, string memberName)
{
    foreach (var project in solution.Projects)
    {
        var compilation = await project.GetCompilationAsync();
        var member = compilation?.GetTypeByMetadataName(typeName)?.GetMembers(memberName)
            .FirstOrDefault(m => m.Locations.Any(l => l.IsInSource));
        if (member is not null)
        {
            return member;
        }
    }

    return null;
}

// The scaffold templates, the docs and the agent guides are in no project, so the workspace never
// sees them. `Type.Old` is unambiguous there and is rewritten; a bare `.Old` or `Old(` might be
// HttpClient's or anyone's, so it is only listed for a person to judge.
static void SweepUncompiledText(string root, string type, string oldName, string newName, bool dryRun)
{
    var qualified = new Regex($@"\b{Regex.Escape(type)}\.{Regex.Escape(oldName)}\b");
    // As a call or a member access, not as a word: "Run it" in a heading is not a reference.
    var bare = new Regex($@"\.{Regex.Escape(oldName)}\b|\b{Regex.Escape(oldName)}\s*[(<]");
    var files = new[] { "src/Rask.Templates", "docs", ".claude/skills" }
        .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
        .Concat(Directory.EnumerateFiles(root, "*.md").Where(f => Path.GetFileName(f) != "CHANGELOG.md"))   // history keeps the old name
        .Append(Path.Combine(root, "llms.txt"))
        .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "NUGET.md", SearchOption.AllDirectories))
        .Where(f => !IsBuildOutput(f) && Path.GetExtension(f) is ".cs" or ".md" or ".txt" or ".ts" or ".csproj");

    foreach (var file in files.Distinct())
    {
        var text = File.ReadAllText(file);
        var rewritten = qualified.Replace(text, $"{type}.{newName}");
        if (rewritten != text)
        {
            Console.WriteLine($"  {(dryRun ? "would rewrite" : "rewrote")} {type}.{oldName} in {Path.GetRelativePath(root, file)}");
            if (!dryRun)
            {
                File.WriteAllText(file, rewritten);
            }
        }

        foreach (var (line, number) in rewritten.Split('\n').Select((l, i) => (l, i + 1)).Where(l => bare.IsMatch(l.l)))
        {
            Console.WriteLine($"  look at {Path.GetRelativePath(root, file)}:{number}: {line.Trim()}");
        }
    }
}

static bool IsBuildOutput(string path) =>
    path.Contains("/obj/", StringComparison.Ordinal) || path.Contains("/bin/", StringComparison.Ordinal)
    || path.Contains("/node_modules/", StringComparison.Ordinal);

static string FindRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (!File.Exists(Path.Combine(dir, "Rask.slnx")))
    {
        dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("Run this inside the Rask repository.");
    }

    return dir;
}
