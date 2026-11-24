using System.Collections.Concurrent;
using FluentValidation;
using FluentValidation.Internal;
using Rask.Core.Forms;
using Rask.Wire;

namespace Rask;

// What FluentValidation states, as Rask.Core and Rask.Data ask it (StatedRules): a control's hints and a column's facets
// from the validators' descriptors, and a save's check by running them. The validators asked are the ones a form finds
// (RaskValidators), so a rule is stated once and asked everywhere.
internal sealed class FluentStatedRules : IStatedRules
{
    internal static FluentStatedRules Instance { get; } = new();

    // Per registry: a hot reload that changes the rules swaps the registry, and with it every answer worked out here.
    private (Registry Registry, ConcurrentDictionary<(Type, string), StatedFacets> Answers) _facets =
        (Registry.Empty, new ConcurrentDictionary<(Type, string), StatedFacets>());

    public bool States(Type type) => RaskValidators.Current.States(type);

    public StatedFacets FacetsOf(Type type, string member)
    {
        var registry = RaskValidators.Current;
        var kept = _facets;
        if (!ReferenceEquals(kept.Registry, registry))
        {
            kept = (registry, new ConcurrentDictionary<(Type, string), StatedFacets>());
            _facets = kept;
        }

        return kept.Answers.GetOrAdd((type, member), static (field, registry) => Read(registry, field.Item1, field.Item2), registry);
    }

    public async ValueTask<IReadOnlyList<FieldFailure>> Check(
        object instance, IReadOnlyCollection<string>? members, IServiceProvider? services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (members is { Count: 0 } || RaskValidators.Find(instance.GetType())?.Invoke(services) is not IValidator validator)
        {
            return [];
        }

        var context = members is null
            ? new ValidationContext<object>(instance)
            : new ValidationContext<object>(instance, new PropertyChain(), new MemberNameValidatorSelector(members));
        var result = await validator.ValidateAsync(context, cancellationToken).ConfigureAwait(false);

        return [.. result.Errors.Select(static e => new FieldFailure(
            e.ErrorMessage, string.IsNullOrEmpty(e.PropertyName) ? [] : [e.PropertyName]))];
    }

    private static StatedFacets Read(Registry registry, Type type, string member)
    {
        if (!registry.States(type))
        {
            return StatedFacets.None;
        }

        if (registry.IsManual(type))
        {
            return Built(RaskValidators.Find(type), null) is { } manual ? StatedFacetReader.Of<object>(manual, member) : StatedFacets.None;
        }

        var facets = StatedFacets.None;
        foreach (var entry in registry.Entries(type))
        {
            if (entry.Projection is { } projection)
            {
                if (projection.Members.TryGetValue(member, out var owned))
                {
                    facets = facets.With(Read(registry, projection.Owner, owned));
                }
            }
            else if (Built(entry.Build, null) is { } validator)
            {
                facets = facets.With(entry.Facets(validator, member));
            }
        }

        return facets;
    }

    // A validator that takes services from the render scope cannot be built without one: it gives no hint, and still runs.
    private static IValidator? Built(Func<IServiceProvider?, object?>? build, IServiceProvider? services)
    {
        if (build is null)
        {
            return null;
        }

#pragma warning disable CA1031 // RaskValidators.Service throws for a scope there is none of: no hint, said above
        try
        {
            return build(services) as IValidator;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
#pragma warning restore CA1031
    }
}
