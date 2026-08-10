using System.Diagnostics;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Tools.BuilderRewrite;

/// <summary>
///     Turns a .csproj into a Roslyn <see cref="CSharpCompilation" /> with the source generators'
///     output already in it.
/// </summary>
/// <remarks>
///     Deliberately not MSBuildWorkspace. The rewrite has to resolve a call site against the REAL
///     generated factory signature, and the factories, the builder entries and the setters are all
///     generator output — so the compilation must contain it. Asking MSBuild to emit it to disk
///     (<c>EmitCompilerGeneratedFiles</c>) and reading it back as ordinary source is both the smaller
///     dependency (no MSBuildLocator, no Workspaces.MSBuild package) and the more honest one: what the
///     tool reads is exactly what the compiler read.
/// </remarks>
internal sealed class ProjectLoader
{
    public sealed record LoadedProject(
        string ProjectPath,
        string ProjectDirectory,
        CSharpCompilation Compilation,
        IReadOnlyList<SyntaxTree> UserTrees);

    private readonly string _configuration;

    public ProjectLoader(string configuration) => _configuration = configuration;

    public LoadedProject Load(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        var dir = Path.GetDirectoryName(projectPath)!;

        // Which framework to analyse. A multi-target project is rewritten once, under its first TFM —
        // the surface is the same under every one of them, and a per-TFM pass would only rewrite the
        // same files twice.
        var eval = Query(
            projectPath, null,
            "-getProperty:TargetFramework", "-getProperty:TargetFrameworks", "-getProperty:AssemblyName");
        var tfm = eval.Property("TargetFramework");
        if (string.IsNullOrEmpty(tfm))
        {
            tfm = eval.Property("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                  ?? throw new InvalidOperationException($"No TargetFramework(s) on {projectPath}");
        }

        // Wipe the generator output before rebuilding it, and the intermediate assembly with it.
        //
        // Both halves matter. Stale generator output is a liability on its own — a file for a component
        // that no longer exists still compiles and still claims a name. But deleting it is not enough:
        // `EmitCompilerGeneratedFiles` is written by CoreCompile, and CoreCompile is INCREMENTAL, so the
        // second run over an unchanged project skips it and the tool reads an empty `generated/`. Every
        // call site then resolves to nothing, every conversion is rejected, and the run silently reports
        // a smaller number than the one before it. Deleting the intermediate assembly is what makes the
        // compile actually happen.
        var intermediate = Path.Combine(dir, "obj", _configuration, tfm);
        var generatedRoot = Path.Combine(intermediate, "generated");
        if (Directory.Exists(generatedRoot))
        {
            Directory.Delete(generatedRoot, recursive: true);
        }

        var assembly = Path.Combine(intermediate, eval.Property("AssemblyName") + ".dll");
        if (File.Exists(assembly))
        {
            File.Delete(assembly);
        }

        var build = Query(
            projectPath,
            tfm,
            "-t:Build",
            "-p:EmitCompilerGeneratedFiles=true",
            "-getItem:ReferencePath",
            "-getItem:Compile",
            "-getProperty:DefineConstants",
            "-getProperty:AllowUnsafeBlocks",
            "-getProperty:Nullable",
            "-getProperty:AssemblyName");

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Preview,
            preprocessorSymbols: build.Property("DefineConstants")
                .Split(';', StringSplitOptions.RemoveEmptyEntries));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var userTrees = new List<SyntaxTree>();
        var allTrees = new List<SyntaxTree>();

        foreach (var path in build.Items("Compile").Select(i => Rooted(dir, i)).Where(File.Exists))
        {
            if (!seen.Add(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(SourceTextOf(path), parseOptions, path);
            allTrees.Add(tree);

            // Only hand-written source under the project directory is rewritable. Linked sources from
            // another project belong to that project's pass; obj/ output belongs to nobody.
            if (IsRewritable(dir, path))
            {
                userTrees.Add(tree);
            }
        }

        if (Directory.Exists(generatedRoot))
        {
            foreach (var path in Directory.EnumerateFiles(generatedRoot, "*.cs", SearchOption.AllDirectories))
            {
                if (seen.Add(path))
                {
                    allTrees.Add(CSharpSyntaxTree.ParseText(SourceTextOf(path), parseOptions, path));
                }
            }
        }

        var references = build.Items("ReferencePath")
            .Select(i => Rooted(dir, i))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            allowUnsafe: string.Equals(build.Property("AllowUnsafeBlocks"), "true", StringComparison.OrdinalIgnoreCase),
            nullableContextOptions: build.Property("Nullable").ToLowerInvariant() switch
            {
                "enable" => NullableContextOptions.Enable,
                "warnings" => NullableContextOptions.Warnings,
                "annotations" => NullableContextOptions.Annotations,
                _ => NullableContextOptions.Disable,
            });

        var compilation = CSharpCompilation.Create(
            build.Property("AssemblyName") is { Length: > 0 } name ? name : Path.GetFileNameWithoutExtension(projectPath),
            allTrees,
            references,
            options);

        return new LoadedProject(projectPath, dir, compilation, userTrees);
    }

    private static Microsoft.CodeAnalysis.Text.SourceText SourceTextOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Microsoft.CodeAnalysis.Text.SourceText.From(stream, System.Text.Encoding.UTF8);
    }

    private static bool IsRewritable(string projectDir, string path)
    {
        if (!path.StartsWith(projectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = path.Substring(projectDir.Length + 1);
        return !relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && !relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && !path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string Rooted(string dir, string value) =>
        Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(dir, value));

    // ---- msbuild -getItem/-getProperty ----------------------------------------------------------

    private sealed record QueryResult(JsonElement Root)
    {
        public string Property(string name) =>
            Root.TryGetProperty("Properties", out var props) && props.TryGetProperty(name, out var v)
                ? v.GetString() ?? ""
                : "";

        public IEnumerable<string> Items(string name)
        {
            if (!Root.TryGetProperty("Items", out var items) || !items.TryGetProperty(name, out var arr))
            {
                yield break;
            }

            foreach (var e in arr.EnumerateArray())
            {
                var value = e.TryGetProperty("FullPath", out var fp) ? fp.GetString()
                    : e.TryGetProperty("Identity", out var id) ? id.GetString()
                    : null;
                if (value is { Length: > 0 })
                {
                    yield return value;
                }
            }
        }
    }

    private QueryResult Query(string projectPath, string? tfm, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add($"-p:Configuration={_configuration}");
        if (tfm is { Length: > 0 })
        {
            psi.ArgumentList.Add($"-p:TargetFramework={tfm}");
        }

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        psi.ArgumentList.Add("-m:1");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-v:q");
        psi.Environment["CI"] = "true";

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var start = stdout.IndexOf('{');
        if (process.ExitCode != 0 || start < 0)
        {
            throw new InvalidOperationException(
                $"msbuild query failed for {projectPath} (exit {process.ExitCode}):\n{stdout}\n{stderr}");
        }

        return new QueryResult(JsonDocument.Parse(stdout[start..]).RootElement);
    }
}
