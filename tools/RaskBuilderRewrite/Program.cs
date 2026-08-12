using Microsoft.CodeAnalysis;
using Rask.Tools.BuilderRewrite;

// Stage E's migration tool. Converts `Foo(A: x, B: y)[…]` into `Foo.A(x).B(y)[…]` and `Foo()` into
// `Foo`, one project at a time, against the real generated factory signatures — and refuses to convert
// anything the compiler will not accept afterwards.
//
//   dotnet run --project tools/RaskBuilderRewrite -- <project.csproj> [--apply] [--report <file>]
//
// Without --apply it only reports. The report names every site it left behind and why, because a
// migration that converts most of a project and NAMES the rest is worth more than one that converts
// everything and breaks something silently.

var projects = new List<string>();
var apply = false;
var reorder = false;
var reflow = false;
var hostify = false;
string? reportPath = null;
var configuration = "Debug";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--apply":
            apply = true;
            break;
        case "--reorder":
            reorder = true;
            break;
        case "--reflow":
            reorder = true;
            reflow = true;
            break;
        case "--hostify":
            hostify = true;
            break;
        case "--report":
            reportPath = args[++i];
            break;
        case "--configuration":
            configuration = args[++i];
            break;
        default:
            projects.Add(args[i]);
            break;
    }
}

if (projects.Count == 0)
{
    Console.Error.WriteLine("usage: RaskBuilderRewrite <project.csproj>... [--apply] [--report <file>]");
    return 2;
}

var report = new List<string>();
var totals = new Dictionary<SiteVerdict, int>();
var convertedTotal = 0;
var hostTotal = 0;
var loader = new ProjectLoader(configuration);

// Keep the file's byte-order mark exactly as it was. Writing one in unconditionally turns every
// rewritten file into a one-extra-byte diff on line 1 that has nothing to do with the migration, and
// hides in a diff that is already large.
static void Write(string path, Microsoft.CodeAnalysis.SyntaxTree tree)
{
    var head = new byte[3];
    using (var probe = File.OpenRead(path))
    {
        _ = probe.Read(head, 0, 3);
    }

    File.WriteAllText(path, tree.GetText().ToString(), new System.Text.UTF8Encoding(head is [0xEF, 0xBB, 0xBF]));
}

foreach (var projectPath in projects)
{
    Console.WriteLine($"== {Path.GetFileNameWithoutExtension(projectPath)}");
    var project = loader.Load(projectPath);

    var surface = SurfaceModel.TryCreate(project.Compilation);
    if (surface is null)
    {
        Console.WriteLine("   no Rask.Core reference — nothing to do");
        continue;
    }

    // Reordering only moves calls that already exist, so it is safe to run against a compilation that
    // does not build — which is exactly the state the required-first rule leaves behind.
    if (reorder)
    {
        var moved = 0;
        foreach (var tree in project.UserTrees)
        {
            var reorderer = new ChainReorderer(surface, reflow);
            var rewritten = reorderer.Run(project.Compilation.GetSemanticModel(tree), tree.GetRoot());
            if (reorderer.Reordered == 0)
            {
                continue;
            }

            moved += reorderer.Reordered;
            Console.WriteLine($"   {Path.GetFileName(tree.FilePath)}: {reorderer.Reordered} reordered");
            if (apply)
            {
                Write(tree.FilePath, rewritten.SyntaxTree);
            }
        }

        Console.WriteLine($"reordered: {moved}{(apply ? "" : " (dry run)")}");
        continue;
    }

    // Hostifying only opts types IN. Making a type a host changes what every name inside it resolves to,
    // so the rewrite has to run against a fresh compilation afterwards — a second invocation of the tool,
    // not a second phase of this one.
    if (hostify)
    {
        var converter = new HostConverter(project.Compilation, surface);
        foreach (var tree in project.UserTrees)
        {
            var host = converter.Convert(tree);
            foreach (var name in host.Blocked)
            {
                report.Add($"blocked {name} — declares a nested component (CS0102 on the injected entry)");
            }

            if (host.Rewritten is null)
            {
                continue;
            }

            hostTotal += host.Hosts.Count;
            foreach (var name in host.Hosts)
            {
                report.Add($"host {name}");
            }

            if (apply)
            {
                Write(tree.FilePath, host.Rewritten);
            }
        }

        continue;
    }

    var rewriter = new FileRewriter(project.Compilation, surface);

    foreach (var tree in project.UserTrees)
    {
        var result = rewriter.Rewrite(tree);
        var relative = Path.GetRelativePath(project.ProjectDirectory, tree.FilePath);

        foreach (var group in result.Sites.Where(s => s.Verdict != SiteVerdict.Convertible)
                     .GroupBy(s => (s.Verdict, s.Detail)))
        {
            totals[group.Key.Verdict] = totals.GetValueOrDefault(group.Key.Verdict) + group.Count();
            var detail = group.Key.Detail is { Length: > 0 } d ? $" ({d})" : "";
            var lines = string.Join(",", group.Take(8).Select(s => s.Line));
            report.Add(
                $"{relative}: {group.Count()} x {group.Key.Verdict}{detail} " +
                $"[{string.Join("/", group.Select(s => s.ComponentName).Distinct().Take(6))}] lines {lines}");
        }

        if (result.Bailout is { } bailout)
        {
            report.Add($"{relative}: FILE SKIPPED — {bailout}");
            Console.WriteLine($"   ! {relative}: {bailout}");
            continue;
        }

        if (result.Rewritten is null || result.Converted.Count == 0)
        {
            continue;
        }

        convertedTotal += result.Converted.Count;
        Console.WriteLine($"   {relative}: {result.Converted.Count}");

        if (apply)
        {
            Write(tree.FilePath, result.Rewritten);
        }
    }
}

Console.WriteLine();
if (hostify)
{
    Console.WriteLine($"hosts:     {hostTotal}{(apply ? "" : " (dry run)")}");
}

Console.WriteLine($"converted: {convertedTotal}{(apply ? "" : " (dry run)")}");
foreach (var (verdict, count) in totals.OrderByDescending(p => p.Value))
{
    Console.WriteLine($"left:      {count,6}  {verdict}");
}

if (reportPath is not null)
{
    File.WriteAllLines(reportPath, report);
    Console.WriteLine($"report:    {reportPath}");
}

return 0;
