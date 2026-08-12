namespace Rask.Cli.Scaffolding;

/// <summary>
/// What a generator produced: the <see cref="Files"/> to write, optional <see cref="Notes"/> to print
/// afterwards (e.g. a template's "run a migration" next steps), and the NuGet <see cref="Packages"/> the
/// output needs — the command adds them to the project automatically.
/// </summary>
/// <remarks>
/// This carried eight more members when <c>rask generate</c> existed: the DbContext splice points, the
/// <c>Program.cs</c> registrations, and the sibling test project a <c>--tests</c> run wired up. They went
/// with the command that produced them. Nothing warns about an unused property on an internal record, so
/// they would have sat here looking like part of the design.
/// </remarks>
internal sealed record ScaffoldResult(IReadOnlyList<ScaffoldFile> Files, string? Notes = null)
{
    /// <summary>Packages the generated code references, added to the project via <c>dotnet add package</c>.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>
    /// The project/solution the command should restore (and guard against overwriting), relative to the target
    /// directory. <c>null</c> means the single-project default (<c>{name}.csproj</c> at the target root). A
    /// multi-project template (e.g. <c>wasm-hosted</c>) sets this to its <c>{name}.slnx</c>, which has no root csproj.
    /// </summary>
    public string? RestoreTarget { get; init; }

    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
