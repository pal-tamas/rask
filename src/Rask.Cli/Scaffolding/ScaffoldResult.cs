namespace Rask.Cli.Scaffolding;

/// <summary>
/// What a generator produced: the <see cref="Files"/> to write and optional <see cref="Notes"/> to print
/// afterwards (e.g. a feature's "register the DbContext / run a migration" next steps).
/// </summary>
internal sealed record ScaffoldResult(IReadOnlyList<ScaffoldFile> Files, string? Notes = null)
{
    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
