using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     The set of entities the model is built from, contributed by each assembly's generated registry.
/// </summary>
/// <remarks>
///     <para>
///         An app writes no <c>DbContext</c> and no <see cref="IEntityTypeConfiguration{TEntity}" />
///         classes: a source generator finds every <see cref="Aggregate{TId}" /> in the compilation and registers
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

    private static AppModel? _appModel;

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

    /// <summary>
    ///     Names the app's <see cref="IRaskModel" />, whose rules then reach every model built through this registry.
    ///     Called by generated code.
    /// </summary>
    /// <typeparam name="TModel">The app's one model class.</typeparam>
    /// <exception cref="InvalidOperationException">Another assembly already named a different one.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void UseModel<TModel>()
        where TModel : IRaskModel =>
        UseModel(typeof(TModel), TModel.Conventions, TModel.Model);

    /// <summary>The app's model rules, as delegates: what <see cref="UseModel{TModel}" /> records.</summary>
    internal static void UseModel(Type type, Action<ModelConfigurationBuilder> conventions, Action<ModelBuilder> model)
    {
        // Two assemblies loading the same type (a hot reload, a test host) is one model; two different types is two
        // sets of rules for one database, and picking either would quietly change the schema.
        if (_appModel is { } current && !string.Equals(current.Type.FullName, type.FullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Both {current.Type.FullName} and {type.FullName} implement IRaskModel. An app has one: move the rules "
                + "of one into the other and delete it.");
        }

        _appModel = new AppModel(type, conventions, model);
    }

    /// <summary>Forgets the app's model rules — for a test that named some.</summary>
    internal static void ForgetModel() => _appModel = null;

    /// <summary>Registers every assembly's strongly-typed id conversions.</summary>
    /// <remarks>
    ///     Separate from <see cref="Apply(Microsoft.EntityFrameworkCore.ModelBuilder)" /> because EF Core asks for conventions before it builds the
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

        // The app's own rules last, so they can say otherwise about anything above.
        _appModel?.Conventions(configurationBuilder);

        return configurationBuilder;
    }

    /// <summary>
    ///     Builds the model: every entity is mapped, then Rask's conventions are applied, then the app's
    ///     <see cref="IRaskModel" />, then each entity's own <c>Configure</c> runs.
    /// </summary>
    /// <remarks>
    ///     The order is the point. Conventions land on entities that exist, and an entity's own
    ///     <c>Configure</c> runs <b>last</b> so it can overrule a convention — replace the soft-delete
    ///     query filter, drop the concurrency token — rather than being quietly overwritten by one.
    ///     The one thing that follows it is the tenant prefix on the indexes <c>Configure</c> declared.
    /// </remarks>
    public static ModelBuilder Apply(ModelBuilder modelBuilder) => Apply(modelBuilder, context: null);

    /// <summary>
    ///     Builds the model, giving the conventions the context they need for the tenant filter.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="context">The context being built, or null when nothing is tenant-scoped.</param>
    /// <returns>The same model builder.</returns>
    public static ModelBuilder Apply(ModelBuilder modelBuilder, DbContext? context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var contribution in Contributions.Values)
        {
            contribution.MapEntities(modelBuilder);
        }

        if (context is null)
        {
            modelBuilder.ApplyRaskConventions();
        }
        else
        {
            modelBuilder.ApplyRaskConventions(context);
        }

        // The app-wide rules before any entity's own, which is the one thing that comes after them.
        _appModel?.Model(modelBuilder);

        foreach (var contribution in Contributions.Values)
        {
            contribution.ApplyConfigurations(modelBuilder);
        }

        // A child partitioned through its parent is settled once more: Configure may have given it a TenantId, or
        // moved the key its filter asks by.
        ModelBuilderExtensions.SettleTenancyThroughParents(modelBuilder, context);

        return modelBuilder;
    }

    private sealed record AppModel(
        Type Type,
        Action<ModelConfigurationBuilder> Conventions,
        Action<ModelBuilder> Model);

    private sealed record Contribution(
        Action<ModelBuilder> MapEntities,
        Action<ModelBuilder> ApplyConfigurations,
        Action<ModelConfigurationBuilder> ConfigureConventions);
}
