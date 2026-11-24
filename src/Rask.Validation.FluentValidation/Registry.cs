using System.Collections.Concurrent;
using FluentValidation;

namespace Rask;

// One set of registrations, read without a lock: RaskValidators swaps the whole instance, so a lookup racing a hot-reload
// refresh sees either the old table or the new one and never a half-built one.
internal sealed class Registry
{
    private readonly Dictionary<Type, ValidatorEntry[]> _entries;
    private readonly Dictionary<Type, Func<IServiceProvider?, object>> _manual;
    private readonly HashSet<Type> _stating;
    private readonly ConcurrentDictionary<Type, Func<IServiceProvider?, object>?> _found = new();

    private Registry(
        Dictionary<Type, ValidatorEntry[]> entries, Dictionary<Type, Func<IServiceProvider?, object>> manual, HashSet<Type> stating)
    {
        _entries = entries;
        _manual = manual;
        _stating = stating;
    }

    internal static Registry Empty { get; } = new([], [], []);

    internal static Registry Of(IEnumerable<ValidatorEntry> generated, Dictionary<Type, Func<IServiceProvider?, object>> manual)
    {
        var entries = generated.GroupBy(static e => e.Model).ToDictionary(static g => g.Key, static g => g.ToArray());
        return new Registry(entries, new Dictionary<Type, Func<IServiceProvider?, object>>(manual), Stating(entries, manual));
    }

    // The types with a rule to run: one with a validator of its own, then each generated model whose aggregate, or the
    // model of one of whose rows, has one — a model that borrows nothing is not given a validator that does nothing.
    private static HashSet<Type> Stating(Dictionary<Type, ValidatorEntry[]> entries, Dictionary<Type, Func<IServiceProvider?, object>> manual)
    {
        var stating = new HashSet<Type>(manual.Keys);
        stating.UnionWith(entries.Where(static e => e.Value.Any(static v => v.Projection is null)).Select(static e => e.Key));

        var projections = entries.Values.SelectMany(static e => e).Where(static e => e.Projection is not null).ToList();
        for (var added = true; added;)
        {
            added = false;
            foreach (var entry in projections.Where(e => !stating.Contains(e.Model) && Reaches(e.Projection!, stating)))
            {
                added |= stating.Add(entry.Model);
            }
        }

        return stating;
    }

    private static bool Reaches(ModelProjection projection, HashSet<Type> stating) =>
        stating.Contains(projection.Owner) || projection.RowTypes.Exists(stating.Contains);

    internal bool States(Type type) => _stating.Contains(type);

    internal IReadOnlyList<ValidatorEntry> Entries(Type type) => _entries.GetValueOrDefault(type) ?? [];

    internal bool IsManual(Type type) => _manual.ContainsKey(type);

    internal Func<IServiceProvider?, object>? Find(Type type) =>
        !_stating.Contains(type) ? null : _found.GetOrAdd(type, static (type, registry) => registry.Factory(type), this);

    private Func<IServiceProvider?, object>? Factory(Type type)
    {
        if (_manual.TryGetValue(type, out var manual))
        {
            return manual;
        }

        var entries = Entries(type);
        if (entries.Count == 0)
        {
            return null;
        }

        return services =>
        {
            var built = entries.Select(e => e.Build(services)).OfType<IValidator>().ToList();
            return entries[0].Combine(built);
        };
    }
}
