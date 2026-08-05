using System.Text.Json;
using Rask.Cqrs;

namespace Rask.Jobs;

/// <summary>
/// Maps a persisted <see cref="Job.Type"/> name back to its CLR type so the
/// <see cref="JobProcessor{TContext}"/> can deserialize + dispatch it. Populated at module load by the
/// <c>Rask.Jobs</c> source generator (one registration per <see cref="IJob"/> type it finds), so there is
/// no runtime <c>Type.GetType</c> / assembly scanning.
/// </summary>
public static class JobSerializerRegistry
{
    private static readonly object _lock = new();

    // Registrations made directly rather than by a generated initializer. Kept apart from the generated
    // groups so a refresh can replace a group's contribution without dropping these.
    private static readonly Dictionary<string, Type> _manual = new(StringComparer.Ordinal);

    // One entry per contributing assembly, keyed by that assembly's generated registry type. Replace
    // swaps a group wholesale, which is what makes a rename drop the old name instead of keeping both.
    private static readonly List<(object Key, (string TypeName, Type Type)[] Items)> _groups = new();

    // The flattened lookup Deserialize reads. Rebuilt under the lock and installed in a single store, so
    // a reader observes either the complete old map or the complete new one, never a half-built one.
    private static volatile IReadOnlyDictionary<string, Type> _types =
        new Dictionary<string, Type>(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a job type by name.</summary>
    public static void RegisterJob(string typeName, Type type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(type);
        lock (_lock)
        {
            _manual[typeName] = type;
            Rebuild();
        }
    }

    /// <summary>
    ///     Installs <paramref name="registrations" /> as the complete set owned by
    ///     <paramref name="groupKey" />, replacing any set previously registered under that key.
    ///     Generated per-assembly initializers call this (passing their own
    ///     <c>typeof(__RaskJobsRegistry)</c>), so re-running one under hot reload swaps that assembly's
    ///     jobs — picking up added, renamed and deleted ones — while leaving every other contributor and
    ///     any direct <see cref="RegisterJob" /> call untouched.
    ///     <para>
    ///         Upserting instead would make a rename additive: the old name would keep resolving to a
    ///         type no longer produced until the process restarted.
    ///     </para>
    ///     <para>
    ///         The key is compared by reference and is never used for reflection, so it attracts no
    ///         trimmer analysis.
    ///     </para>
    /// </summary>
    public static void Replace(object groupKey, IEnumerable<(string TypeName, Type Type)> registrations)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(registrations);

        var items = registrations as (string TypeName, Type Type)[] ?? registrations.ToArray();
        lock (_lock)
        {
            for (var i = 0; i < _groups.Count; i++)
            {
                if (!ReferenceEquals(_groups[i].Key, groupKey))
                {
                    continue;
                }

                // An unrelated hot reload re-runs every RefreshAll(); skip the rebuild when this
                // contributor's jobs are unchanged.
                if (_groups[i].Items.AsSpan().SequenceEqual(items))
                {
                    return;
                }

                _groups[i] = (groupKey, items);
                Rebuild();
                return;
            }

            _groups.Add((groupKey, items));
            Rebuild();
        }
    }

    // Caller holds _lock. Manual registrations are applied last so an explicit one is never clobbered by
    // a generated refresh.
    private static void Rebuild()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (_, items) in _groups)
        {
            foreach (var (typeName, type) in items)
            {
                map[typeName] = type;
            }
        }

        foreach (var (typeName, type) in _manual)
        {
            map[typeName] = type;
        }

        _types = map;
    }

    /// <summary>Serializes a job to its stored (type-name, JSON-payload) pair.</summary>
    public static (string Type, string Payload) Serialize(IJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var type = job.GetType();
        return (TypeName(type), JsonSerializer.Serialize(job, type, Json));
    }

    // Match the name the source generator registers (Roslyn's ToDisplayString is dot-separated even for a
    // nested type) — Type.FullName uses '+' between a nesting type and its nested type, so normalize it,
    // otherwise a nested IJob would be stored under a name the registry never has and silently dead-letter.
    internal static string TypeName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    /// <summary>Rehydrates a stored job as a dispatchable command, or <c>null</c> if its type isn't registered.</summary>
    public static ICommand? Deserialize(string typeName, string payload)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(payload);
        return _types.TryGetValue(typeName, out var type)
            ? JsonSerializer.Deserialize(payload, type, Json) as ICommand
            : null;
    }
}
