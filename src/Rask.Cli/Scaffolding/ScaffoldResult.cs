namespace Rask.Cli.Scaffolding;

internal sealed record ScaffoldResult(IReadOnlyList<ScaffoldFile> Files, string? Notes = null)
{
    /// <summary>Commands run before <see cref="Files" /> are written, in order.</summary>
    public IReadOnlyList<ExternalScaffold> ExternalScaffolds { get; init; } = [];

    /// <summary>Edits applied after <see cref="Files" /> are written, in order.</summary>
    public IReadOnlyList<ScaffoldPatch> Patches { get; init; } = [];

    /// <summary>Packages the generated code references, added to the project via <c>dotnet add package</c>.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>
    /// The project/solution the command should restore (and guard against overwriting), relative to the target
    /// directory. <c>null</c> means the single-project default (<c>{name}.csproj</c> at the target root). A
    /// multi-project template — a front-end one, with its client beside an ASP.NET host — sets this to its
    /// <c>{name}.slnx</c>, which has no root csproj.
    /// </summary>
    public string? RestoreTarget { get; init; }

    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
