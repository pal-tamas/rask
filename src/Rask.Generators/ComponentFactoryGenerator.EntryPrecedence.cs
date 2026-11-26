using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // Which referenced library is NEARER when two of them publish an entry of one simple name.
    //
    // The app's own component already beats a referenced library's in the app's compilation (the `taken`
    // set in UsableExternalEntries). One step further out the same pair arrives as two REFERENCED
    // libraries: a test project references the app AND, through it, the app's Rask.Dashboard, and both
    // publish a `NotFoundPage`. The app references the dashboard, so inside the app the name was already
    // the app's — the test project keeps that answer instead of losing both to RASK040. Two libraries that
    // do not reference each other have no such order, so they still collide.
    private static EquatableArray<EntryHostDependency> EntryHostDependencies(
        List<(IAssemblySymbol Assembly, string HostFqn)> libraryHosts, IAssemblySymbol framework)
    {
        if (libraryHosts.Count < 2)
        {
            return default;
        }

        var dependencies = new List<EntryHostDependency>();
        foreach (var (assembly, hostFqn) in libraryHosts)
        {
            var reached = ReachableRaskAssemblies(assembly, framework);
            foreach (var (other, otherFqn) in libraryHosts)
            {
                if (!SymbolEqualityComparer.Default.Equals(assembly, other) && reached.Contains(other))
                {
                    dependencies.Add(new EntryHostDependency(hostFqn, otherFqn));
                }
            }
        }

        if (dependencies.Count == 0)
        {
            return default;
        }

        dependencies.Sort(static (a, b) =>
        {
            var byDependent = string.CompareOrdinal(a.DependentHostFqn, b.DependentHostFqn);
            return byDependent != 0 ? byDependent : string.CompareOrdinal(a.DependencyHostFqn, b.DependencyHostFqn);
        });
        return new EquatableArray<EntryHostDependency>(dependencies.ToImmutableArray());
    }

    // Every assembly `root` references, directly or through others. The walk only descends through
    // assemblies that reference Rask.Core themselves: an entry host derives its components from Component,
    // so a library carrying one always references Rask.Core, and the BCL is never walked. This runs on the
    // compilation provider, i.e. on every edit, so the bound matters.
    private static HashSet<IAssemblySymbol> ReachableRaskAssemblies(IAssemblySymbol root, IAssemblySymbol framework)
    {
        var reached = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IAssemblySymbol>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var next = pending.Pop().Modules.SelectMany(static m => m.ReferencedAssemblySymbols)
                .Where(r => reached.Add(r) && ReferencesFramework(r, framework));
            foreach (var referenced in next)
            {
                pending.Push(referenced);
            }
        }

        return reached;
    }

    private static bool ReferencesFramework(IAssemblySymbol assembly, IAssemblySymbol framework) =>
        assembly.Modules.Any(m => m.ReferencedAssemblySymbols.Contains(framework, SymbolEqualityComparer.Default));

    // The one host of a colliding name that every other host in the group is a dependency of — or null
    // when there is no single such host (two unrelated libraries), which leaves the name to RASK040.
    private static string? NearestEntryHost(HashSet<string> hosts, EquatableArray<EntryHostDependency> dependencies)
    {
        if (dependencies.Count == 0)
        {
            return null;
        }

        var shadowed = new HashSet<string>(
            dependencies.Where(d => hosts.Contains(d.DependentHostFqn) && hosts.Contains(d.DependencyHostFqn))
                .Select(static d => d.DependencyHostFqn),
            StringComparer.Ordinal);

        var remaining = hosts.Where(h => !shadowed.Contains(h)).ToList();
        return shadowed.Count > 0 && remaining.Count == 1 ? remaining[0] : null;
    }

    // Every bare entry name this compilation's own components claim, including the ones RASK040 withholds
    // from all of them: a collision between two of the author's components is theirs to settle, and a
    // referenced library's same-named entry must not settle it for them.
    private static HashSet<string> OwnClaimedNames(ImmutableArray<Candidate> candidates) =>
        candidates.IsDefaultOrEmpty
            ? new HashSet<string>(StringComparer.Ordinal)
            : new(candidates.Where(static c => c.Group is null && CanHaveEntry(c, EmptyNames)).Select(static c => c.EntryName),
            StringComparer.Ordinal);

    // `DependentHostFqn`'s assembly references `DependencyHostFqn`'s, so its entries are the nearer ones.
    private readonly record struct EntryHostDependency(string DependentHostFqn, string DependencyHostFqn);
}
