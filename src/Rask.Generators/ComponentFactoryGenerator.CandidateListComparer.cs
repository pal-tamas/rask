using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
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
