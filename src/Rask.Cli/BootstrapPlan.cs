namespace Rask.Cli;

/// <summary>
/// An ordered plan for turning a host into one <c>rask deploy</c> can use.
///
/// <para>The split between <see cref="Preparation"/> and <see cref="Risky"/> is the safety contract:
/// preparation can't cost you access to the box, <see cref="Risky"/> can. Everything in
/// <see cref="Risky"/> runs only after a fresh connection has proved the <see cref="NewUser"/> login
/// works, and only behind the rollback guard.</para>
/// </summary>
/// <param name="NewUser">The login to deploy as once the plan has run, or <c>null</c> to keep the current one.</param>
/// <param name="Warnings">Things we deliberately refused to do, and why — never silently dropped.</param>
internal sealed record BootstrapPlan(
    IReadOnlyList<BootstrapStep> Preparation,
    IReadOnlyList<BootstrapStep> Risky,
    string? NewUser,
    IReadOnlyList<string> Warnings)
{
    public bool IsEmpty => Preparation.Count == 0 && Risky.Count == 0;

    public IEnumerable<BootstrapStep> AllSteps => Preparation.Concat(Risky);
}
