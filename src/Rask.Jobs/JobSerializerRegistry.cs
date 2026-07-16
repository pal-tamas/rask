using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, Type> Types = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a job type by name. Called by the generated module initializer.</summary>
    public static void RegisterJob(string typeName, Type type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(type);
        Types[typeName] = type;
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
        return Types.TryGetValue(typeName, out var type)
            ? JsonSerializer.Deserialize(payload, type, Json) as ICommand
            : null;
    }
}
