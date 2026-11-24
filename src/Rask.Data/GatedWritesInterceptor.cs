using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data;

/// <summary>
///     Before a save: a member gated with <c>b.Gated(…)</c> whose gate is shut for the work in flight is not written — a
///     changed row keeps the value it holds, and an added one stores none.
/// </summary>
/// <param name="services">The app's services: its gate rules, and the scope to fall back to outside one.</param>
internal sealed class GatedWritesInterceptor(IServiceProvider services) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Hold(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Hold(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Hold(DbContext? context)
    {
        if (context is null || !GatedMembers.Any)
        {
            return;
        }

        // Each gate is asked once per save, however many rows are behind it.
        var answers = new Dictionary<object, bool>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var (member, gate) in GatedMembers.Of(entry.Metadata.ClrType))
            {
                if (!answers.TryGetValue(gate, out var open))
                {
                    answers[gate] = open = IsOpen(gate);
                }

                if (!open)
                {
                    Keep(entry, member);
                }
            }
        }
    }

    private bool IsOpen(object gate)
    {
        var rule = services.GetServices<GateRule>().LastOrDefault(r => r.GateType.IsInstanceOfType(gate))
                   ?? throw new InvalidOperationException(
                       $"'{gate}' gates a member, and nothing says when a {gate.GetType().Name} is open. Say it once, " +
                       $"where the services are added: services.AddRaskGate<{gate.GetType().Name}>((sp, gate) => …).");

        return rule.IsOpen(Db.ScopeServices ?? services, gate);
    }

    // An added row stores nothing there; a changed one keeps what it was loaded with.
    private static void Keep(EntityEntry entry, string member)
    {
        if (entry.Metadata.FindProperty(member) is not null)
        {
            var property = entry.Property(member);
            if (entry.State == EntityState.Added)
            {
                property.CurrentValue = null;
            }
            else
            {
                property.CurrentValue = property.OriginalValue;
                property.IsModified = false;
            }

            return;
        }

        var complex = entry.ComplexProperty(member);
        if (entry.State == EntityState.Added)
        {
            complex.CurrentValue = null;
            return;
        }

        // The value object as it was loaded, rebuilt from the original values: the instance in hand may be a new one, or none.
        var loaded = entry.OriginalValues.ToObject();
        complex.CurrentValue = complex.Metadata.GetGetter().GetClrValueUsingContainingEntity(loaded);
        complex.IsModified = false;
    }
}
