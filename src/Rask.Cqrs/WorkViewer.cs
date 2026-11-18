namespace Rask.Background;

/// <summary>
///     Who reads a bell, and so which work they may see (#1368): their own, in their tenant — and their tenant's shared work
///     (<c>.ByTenant()</c>) of every kind but those whose handler's <c>[Authorize]</c> refuses them. Decided once, where they
///     are admitted, so a live event is matched without a wait.
/// </summary>
/// <param name="User">The signed-in user.</param>
/// <param name="Tenant">Their tenant, or null: with none, nothing shared reaches them.</param>
/// <param name="Refused">The job kinds (registered type names) whose shared work they may not see.</param>
internal sealed record WorkViewer(Guid User, Guid? Tenant, IReadOnlySet<string> Refused)
{
    /// <summary>Whether they may see <paramref name="work" />.</summary>
    public bool May(WorkReported work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (work.Tenant != Tenant)
        {
            return false;
        }

        if (work.Owner == User)
        {
            return true;
        }

        return work.Shared && Tenant is not null && work.Owner is not null && work.Kind is { } kind && !Refused.Contains(kind);
    }
}
