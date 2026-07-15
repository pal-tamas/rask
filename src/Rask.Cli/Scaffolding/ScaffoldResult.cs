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

    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
