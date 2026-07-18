namespace Rask.Cli.Scaffolding;

/// <summary>
/// What a generator produced: the <see cref="Files"/> to write, optional <see cref="Notes"/> to print
/// afterwards (e.g. a feature's "register the DbContext / run a migration" next steps), and the NuGet
/// <see cref="Packages"/> the output needs — the command adds them to the project automatically.
/// </summary>
internal sealed record ScaffoldResult(IReadOnlyList<ScaffoldFile> Files, string? Notes = null)
{
    /// <summary>Packages the generated code references, added to the project via <c>dotnet add package</c>.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>
    /// <c>using</c> namespaces the <see cref="ProgramRegistrations"/> need. The command adds any that are
    /// missing to <c>Program.cs</c> when it wires the registrations in. Empty for generators that don't
    /// register services.
    /// </summary>
    public IReadOnlyList<string> ProgramUsings { get; init; } = [];

    /// <summary>
    /// Service-registration statements the command inserts into <c>Program.cs</c> after the scaffold is
    /// written (idempotently — a statement already present is left alone). Each entry may span multiple
    /// lines. When <c>Program.cs</c> can't be found or understood, they're printed as a manual fallback.
    /// </summary>
    public IReadOnlyList<string> ProgramRegistrations { get; init; } = [];

    /// <summary>
    /// The project/solution the command should restore (and guard against overwriting), relative to the target
    /// directory. <c>null</c> means the single-project default (<c>{name}.csproj</c> at the target root). A
    /// multi-project template (e.g. <c>wasm-hosted</c>) sets this to its <c>{name}.sln</c>, which has no root csproj.
    /// </summary>
    public string? RestoreTarget { get; init; }

    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
