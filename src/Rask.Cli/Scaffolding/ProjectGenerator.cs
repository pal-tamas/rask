namespace Rask.Cli.Scaffolding;

/// <summary>
/// Generates a whole project directly (the CLI is the scaffolding authority — no <c>dotnet new</c> /
/// Rask.Templates). Each template is hand-ported here: files are emitted with the placeholder namespace
/// <c>Company.RaskServer</c> and a final pass rewrites it (and the csproj filename) to the app name, so the
/// content reads exactly like the source template. Flag conditionals (<c>--auth</c>/<c>--pwa</c>/<c>--cqrs</c>/
/// <c>--docker</c>) are generation logic, not <c>#if</c> markers. Package references are pinned to the
/// version the caller passes (the CLI's own version).
/// </summary>
/// <remarks>
/// One template per partial file — <c>.Server.cs</c>, <c>.Wasm.cs</c>, <c>.WasmHosted.cs</c>,
/// <c>.Native.cs</c> — with the content more than one of them emits in <c>.Shared.cs</c>. The multi-project
/// <c>wasm-hosted</c> template emits a Client/Server/Shared trio and restores the generated solution.
/// </remarks>
internal static partial class ProjectGenerator
{
    private const string NameToken = "Company.RaskServer";

    /// <summary>
    /// Materialise a single-project template: the placeholder namespace becomes <paramref name="name"/> in
    /// every file's content, and in the paths too (so <c>{NameToken}.csproj</c> becomes <c>{name}.csproj</c>).
    /// </summary>
    private static List<ScaffoldFile> Materialize(
        string targetDirectory, string name, IEnumerable<(string Path, string Content)> files) =>
        files.Select(f => new ScaffoldFile(
            System.IO.Path.Combine(targetDirectory, f.Path.Replace(NameToken, name, StringComparison.Ordinal)),
            f.Content.Replace(NameToken, name, StringComparison.Ordinal))).ToList();

    /// <summary>
    /// Materialise a multi-project template, where each file declares which namespace the placeholder becomes
    /// (a two-project solution has a <c>{name}.Host</c> and a <c>{name}.Wasm</c>). Paths are used as given —
    /// the project name is already a directory segment in them, so a blanket token replace would double it up.
    /// </summary>
    private static List<ScaffoldFile> Materialize(
        string targetDirectory, IEnumerable<(string Path, string Content, string Namespace)> files) =>
        files.Select(f => new ScaffoldFile(
            System.IO.Path.Combine(targetDirectory, f.Path),
            f.Content.Replace(NameToken, f.Namespace, StringComparison.Ordinal))).ToList();
}
