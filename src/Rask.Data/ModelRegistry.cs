using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The set of entities the model is built from, contributed by each assembly's generated registry.
/// </summary>
/// <remarks>
///     <para>
///         An app writes no <c>DbContext</c> and no <see cref="IEntityTypeConfiguration{TEntity}" />
///         classes: a source generator finds every <see cref="Model" /> in the compilation and registers
///         a contribution here, which <see cref="RaskDbContext" /> replays in <c>OnModelCreating</c>.
///     </para>
///     <para>
///         Generated code, not reflection. Nothing here scans an assembly or looks a method up by name,
///         so the model survives a trimmed publish — the failure this avoids is an entity the trimmer
///         dropped, which produces a missing table and a green build.
///     </para>
///     <para>
///         Registration is per-assembly and <b>replacing</b>: a group re-registering (a hot reload, a
///         test host loading the same assembly twice) supersedes its previous contribution rather than
///         doubling it.
///     </para>
/// </remarks>
public static class ModelRegistry
{
    private static readonly ConcurrentDictionary<Type, Contribution> Contributions = new();

    /// <summary>Whether any assembly has contributed entities.</summary>
    /// <remarks>
    ///     <c>false</c> means the app declared no entity, which is why <see cref="RaskDbContext" /> maps
    ///     nothing — the honest answer to "my table is missing" when the entity lives in an assembly the
    ///     generator never ran over.
    /// </remarks>
    public static bool IsEmpty => Contributions.IsEmpty;

    /// <summary>
    ///     Replaces <paramref name="group" />'s contribution to the model. Called by generated code.
    /// </summary>
    /// <param name="group">The generated registry class, standing for its assembly.</param>
    /// <param name="mapEntities">Adds each of the assembly's entities to the model.</param>
    /// <param name="applyConfigurations">
    ///     Runs each entity's own static <c>Configure</c>, for those that declare one.
    /// </param>
    /// <param name="configureConventions">
    ///     Registers the value converters for the assembly's strongly-typed ids, so every property of one
    ///     — key, foreign key, nullable — is converted without being named individually.
    /// </param>
    public static void Replace(
        Type group,
        Action<ModelBuilder> mapEntities,
        Action<ModelBuilder> applyConfigurations,
        Action<ModelConfigurationBuilder> configureConventions)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(mapEntities);
        ArgumentNullException.ThrowIfNull(applyConfigurations);
        ArgumentNullException.ThrowIfNull(configureConventions);

        Contributions[group] = new Contribution(mapEntities, applyConfigurations, configureConventions);
    }

    /// <summary>Registers every assembly's strongly-typed id conversions.</summary>
    /// <remarks>
    ///     Separate from <see cref="Apply" /> because EF Core asks for conventions before it builds the
    ///     model — a converter registered in <c>OnModelCreating</c> is already too late for the key it was
    ///     meant to convert.
    /// </remarks>
    public static ModelConfigurationBuilder ApplyConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        foreach (var contribution in Contributions.Values)
        {
            contribution.ConfigureConventions(configurationBuilder);
        }

        return configurationBuilder;
    }

    /// <summary>
    ///     Builds the model: every entity is mapped, then Rask's conventions are applied, then each
    ///     entity's own <c>Configure</c> runs.
    /// </summary>
    /// <remarks>
    ///     The order is the point. Conventions land on entities that exist, and an entity's own
    ///     <c>Configure</c> runs <b>last</b> so it can overrule a convention — replace the soft-delete
    ///     query filter, drop the concurrency token — rather than being quietly overwritten by one.
    /// </remarks>
    public static ModelBuilder Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var contribution in Contributions.Values)
        {
            contribution.MapEntities(modelBuilder);
        }

        modelBuilder.ApplyRaskConventions();

        foreach (var contribution in Contributions.Values)
        {
            contribution.ApplyConfigurations(modelBuilder);
        }

        return modelBuilder;
    }

    private sealed record Contribution(
        Action<ModelBuilder> MapEntities,
        Action<ModelBuilder> ApplyConfigurations,
        Action<ModelConfigurationBuilder> ConfigureConventions);
}
