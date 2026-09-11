using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Rask.Generators.External.PackageIslands;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    /// <summary>
    ///     Adds the chain steps a package island gets from its committed props snapshot.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         After <c>Collect()</c> rather than inside <c>GetCandidate</c>, and that is forced: the candidate is
    ///         built in the syntax transform, which cannot read additional files, and the snapshot is one. So the
    ///         candidate carries the symbol-side facts (<see cref="IslandFacts" />) and this pairs them with the
    ///         parsed snapshots once both are in hand.
    ///     </para>
    ///     <para>
    ///         Every island is resolved in one <see cref="PackageIslandProps.ResolveAll" /> call, which
    ///         <c>ExternalGenerator</c> makes too over the same set, to declare the properties these steps set.
    ///         One resolver over one set is what keeps a step from existing without its property, and a generated
    ///         type name from differing between the two halves — both compile errors in code the author never
    ///         wrote.
    ///     </para>
    ///     <para>
    ///         Returns the SAME array when there is nothing to add, so a project with no snapshots — every
    ///         project that is not using a package island — keeps every downstream output cached exactly as
    ///         before.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<Candidate> WithPackageProps(
        ImmutableArray<Candidate> candidates,
        EquatableArray<PropsSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return candidates;
        }

        var paired = new List<(IslandFacts Facts, PropsSnapshot Snapshot)>();
        foreach (var candidate in candidates)
        {
            if (candidate.Package is { } facts && PackageIslandProps.Find(snapshots, facts) is { } snapshot)
            {
                paired.Add((facts, snapshot));
            }
        }

        if (paired.Count == 0)
        {
            return candidates;
        }

        var resolved = PackageIslandProps.ResolveAll(paired);

        Candidate[]? changed = null;
        for (var i = 0; i < candidates.Length; i++)
        {
            if (candidates[i].Package is not { } facts
                || !resolved.TryGetValue(facts, out var island)
                || island.Verdict != PackageVerdict.Usable)
            {
                // An unusable snapshot is reported by ExternalGenerator, which also generates nothing for it — so
                // neither half adds a step the other would not back with a property.
                continue;
            }

            changed ??= candidates.ToArray();
            changed[i] = WithPackageProps(candidates[i], island);
        }

        return changed is null ? candidates : ImmutableArray.Create(changed);
    }

    private static Candidate WithPackageProps(Candidate candidate, PackageIsland island)
    {
        var properties = candidate.Properties.ToList();
        var memberNames = new SortedSet<string>(candidate.MemberNames, StringComparer.Ordinal);

        foreach (var prop in island.Props)
        {
            // A prop the author declared by hand already has its step, from the real property.
            if (prop.DeclaredByUser
                || properties.Any(p => string.Equals(p.Name, prop.ClrName, StringComparison.Ordinal)))
            {
                continue;
            }

            var isCallback = prop.Callback is not null;
            properties.Add(new PropInfo(
                prop.ClrName,
                prop.ChainTypeFqn,
                // Optional unless the package requires it — the same default a Blazor island's parameters
                // get, and for the same reason: a package declares far more than any one call site sets.
                IsNullable: !prop.IsRequired,
                HasInitializer: false,
                UserMarkedRequired: prop.IsRequired,
                InheritanceDepth: 0,
                candidate.DeclFilePath,
                candidate.DeclSpanStart,
                candidate.DeclSpanLength,
                IsAutoRerenderDelegate: isCallback,
                IsTypeParameter: false,
                IsBoundInterfaceProp: false,
                IsDelegate: isCallback,
                InitializerDefault: null,
                IsInitOnly: false,
                IsSharedSurfaceProp: false,
                HasDerivedSetter: false,
                prop.Summary));

            // A prop named after a component (Label, Title, Select — ordinary names for a UI library) would
            // otherwise land beside the entry injected under that name in the same class: CS0102. Naming it
            // here makes the injection skip it, through the collision check every other member goes through.
            memberNames.Add(prop.ClrName);
        }

        return candidate with
        {
            Properties = new EquatableArray<PropInfo>(properties),
            MemberNames = new EquatableArray<string>(memberNames),
        };
    }

    /// <summary>Compares collected candidate lists by content, so an unchanged list stays cached.</summary>
    private sealed class CandidateListComparer : IEqualityComparer<ImmutableArray<Candidate>>
    {
        public static readonly CandidateListComparer Instance = new();

        public bool Equals(ImmutableArray<Candidate> x, ImmutableArray<Candidate> y) =>
            x.AsSpan().SequenceEqual(y.AsSpan());

        public int GetHashCode(ImmutableArray<Candidate> obj)
        {
            unchecked
            {
                var hash = 17;
                foreach (var candidate in obj)
                {
                    hash = (hash * 31) + candidate.GetHashCode();
                }

                return hash;
            }
        }
    }
}
