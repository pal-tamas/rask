using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Rask.Core.Diagnostics;

namespace Rask.Core.Live;

/// <summary>
///     The per-session <see cref="IPersistentState" /> bag. Scoped: one instance per live session, resolved
///     from that session's DI scope alongside <c>RouteState</c> and the rest.
/// </summary>
/// <remarks>
///     Values are held as UTF-8 JSON rather than as objects. That costs a serialize on write, and buys two
///     things the object form can't: the bag is already in its wire shape when the handoff record is built
///     (no walk over live objects at shutdown, when there is least time to spare), and a value whose type
///     changed between deploys fails to read back as a miss instead of poisoning the whole record.
/// </remarks>
internal sealed class PersistentState : IPersistentState
{
    internal const string TrimWarning =
        "Persisted state is serialized with reflection-based System.Text.Json. Use the JsonTypeInfo<T> overloads in a trimmed or AOT app.";

    /// <summary>
    ///     Total budget across keys and values. The bag rides the wire inside a protected token, so this is
    ///     a wire-size bound, not a memory one — 16 KB of JSON is already a large thing to push through a
    ///     reconnect, and a page needing more than that is describing its data rather than its state.
    /// </summary>
    internal const int DefaultMaxBytes = 16 * 1024;

    // Web defaults (camelCase) to match every other JSON surface the framework writes.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);

    /// <summary>Bytes currently accounted for — every key's UTF-8 length plus its value's.</summary>
    private int _bytes;

    internal int MaxBytes { get; set; } = DefaultMaxBytes;

    /// <summary>
    ///     Bumped on every mutation. The handoff layer keeps the last version it issued a token for, so an
    ///     idle session never re-signs and re-sends a record that hasn't moved.
    /// </summary>
    internal int Version { get; private set; }

    /// <summary>
    ///     Set once the bag has grown past <see cref="MaxBytes" />, and never cleared by a later shrink —
    ///     a session that was ever over budget stays unresumable for its lifetime rather than flickering
    ///     between resumable and not as keys come and go.
    /// </summary>
    internal bool Overflowed { get; private set; }

    /// <summary>The raw bag, for the handoff layer to serialize. Not a copy — do not mutate.</summary>
    internal IReadOnlyDictionary<string, byte[]> Entries => _entries;

    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    public void Persist<T>(string key, T value) =>
        Store(key, JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions));

    public void Persist<T>(string key, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        Store(key, JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
    }

    [RequiresUnreferencedCode(TrimWarning)]
    [RequiresDynamicCode(TrimWarning)]
    public bool TryGet<T>(string key, out T? value)
    {
        if (!TryReadBytes(key, out var bytes))
        {
            value = default;
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(bytes, SerializerOptions);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    public bool TryGet<T>(string key, JsonTypeInfo<T> typeInfo, out T? value)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (!TryReadBytes(key, out var bytes))
        {
            value = default;
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize(bytes, typeInfo);
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!_entries.TryGetValue(key, out var existing))
        {
            return false;
        }

        _bytes -= EntryCost(key, existing.Length);
        _entries.Remove(key);
        Version++;
        return true;
    }

    public void Clear()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _entries.Clear();
        _bytes = 0;
        Version++;
    }

    /// <summary>
    ///     Seeds the bag from a handoff record on a rebuild. Deliberately not a public API: the app writes
    ///     through <see cref="Persist{T}(string,T)" /> and reads through <see cref="TryGet{T}(string,out T)" />;
    ///     only the framework restores. Resets the version so the freshly-seeded session doesn't immediately
    ///     re-issue a token for state it was just handed.
    /// </summary>
    internal void Restore(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        _entries.Clear();
        _bytes = 0;
        foreach (var (key, value) in entries)
        {
            _entries[key] = value;
            _bytes += EntryCost(key, value.Length);
        }

        Overflowed = _bytes > MaxBytes;
        Version = 0;
    }

    private void Store(string key, byte[] payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var previous = _entries.TryGetValue(key, out var existing) ? EntryCost(key, existing.Length) : 0;
        _entries[key] = payload;
        _bytes += EntryCost(key, payload.Length) - previous;
        Version++;

        if (_bytes <= MaxBytes || Overflowed)
        {
            return;
        }

        // Keep the write. Refusing it would lose state the app believes it stored, and throwing would turn
        // a size budget into an exception at an arbitrary call site. Instead the session gives up its
        // resume token and falls back to the reload it would have had without any of this — a degradation,
        // not a failure. Deduped on a constant key: this is a property of the app's code, not of one
        // session, and a per-session key would exhaust the shared ReportOnce cap under load.
        Overflowed = true;
        var bytes = _bytes;
        var max = MaxBytes;
        RaskDiagnostics.ReportOnce(
            "persistentstate:overflow",
            RaskLogLevel.Warning,
            "Rask.Live",
            () => $"Persisted state is {bytes} bytes, over the {max}-byte budget, so affected sessions "
                  + "will not be resumable and will reload instead. Persist identifiers and selections "
                  + "rather than the data they resolve to.");
    }

    // Both TryGet overloads swallow JsonException on the read. The bytes came from this app's own protected
    // token, so a failure here is not tampering (that is rejected before we get here) — it is a deploy that
    // changed the shape of a persisted type while a token written by the previous version was still in
    // flight. Reading it as a miss lets the page rebuild with a default instead of throwing out of user
    // code during a reconnect.
    private bool TryReadBytes(string key, out byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _entries.TryGetValue(key, out bytes!);
    }

    // Keys ride the wire alongside their values, so they count. Without this, many tiny keys would pass a
    // value-only budget while producing a token far larger than it allows.
    private static int EntryCost(string key, int valueLength) => Encoding.UTF8.GetByteCount(key) + valueLength;
}
