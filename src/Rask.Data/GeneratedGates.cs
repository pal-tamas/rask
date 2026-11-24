using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Whether a member gated with <c>b.Gated(x =&gt; x.Card, Module.Tachograph)</c> is shut for the work in flight — what a
///     generated form asks to leave its field out, and its value unread.
/// </summary>
/// <remarks>Called by generated code, which lives in the application's assembly. Not an API to write against.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedGates
{
    /// <summary>Whether <paramref name="member" /> of <paramref name="entity" /> is behind a gate that is shut now.</summary>
    /// <param name="entity">The entity that gates the member.</param>
    /// <param name="member">The member's name.</param>
    /// <returns>
    ///     True when its gate is shut; false when it is open, the member is not gated, or nothing says yet when its kind of
    ///     gate is open — the save, which refuses that, is where it is said.
    /// </returns>
    public static bool IsShut(Type entity, string member)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(member);
        if (!GatedMembers.Any || GatedMembers.Of(entity).FirstOrDefault(m => string.Equals(m.Key, member, StringComparison.Ordinal)) is not { Value: { } gate })
        {
            return false;
        }

        var services = Db.ScopeServices ?? Ambient.Services ?? AppServices();
        var rule = services?.GetServices<GateRule>().LastOrDefault(r => r.GateType.IsInstanceOfType(gate));
        return rule is not null && !rule.IsOpen(services!, gate);
    }

    // Outside any scope: the services the app's own context saves with, as the save asks its gates.
    private static IServiceProvider? AppServices()
    {
        if (!Db.HasContext)
        {
            return null;
        }

        using var context = Db.CreateOwnContext();
        return BulkInterceptors.Of<GatedWritesInterceptor>(context)?.Services;
    }
}
