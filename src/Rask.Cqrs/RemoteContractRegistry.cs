namespace Rask.Cqrs;

/// <summary>
///     The wire-side twin of <see cref="CqrsRegistry" />: every message that has a generated codec,
///     keyed both by CLR type and by wire name. Populated at module load by the code the Rask.Cqrs
///     source generator emits, then read by the client transport (to decide what to send) and by the
///     server endpoint (to decide what it is willing to receive). This type is public only so
///     generated code can call into it; you do not use it directly.
/// </summary>
/// <remarks>
///     A contract's presence here says only that the message <em>can</em> be encoded — not that it is
///     reachable. What is reachable is decided per host: the server exposes the contracts it has a
///     handler for, and the client sends the ones it does not.
/// </remarks>
public static class RemoteContractRegistry
{
    private static readonly object Gate = new();

    // One entry per contributing assembly, keyed by that assembly's generated registry type, so a
    // hot-reload re-run swaps that assembly's contribution instead of merging into it — the same
    // shape (and the same reason) as CqrsRegistry's request groups.
    private static readonly List<(object Key, RemoteContract[] Items)> Groups = [];

    // Rebuilt under the gate and installed in a single store, so a dispatch in flight observes either
    // the complete old pair of tables or the complete new one.
    private static volatile IReadOnlyDictionary<Type, RemoteContract> _byType =
        new Dictionary<Type, RemoteContract>();

    private static volatile IReadOnlyDictionary<string, RemoteContract> _byName =
        new Dictionary<string, RemoteContract>(StringComparer.Ordinal);

    private static volatile RemoteContract[] _all = [];

    /// <summary>Every registered contract, in no meaningful order.</summary>
    public static IReadOnlyList<RemoteContract> All => _all;

    /// <summary>
    ///     Installs <paramref name="contracts" /> as the complete set owned by
    ///     <paramref name="groupKey" />, replacing anything that key contributed before.
    /// </summary>
    /// <param name="groupKey">The contributing assembly's generated registry type.</param>
    /// <param name="contracts">That assembly's complete contract set.</param>
    public static void Replace(object groupKey, IEnumerable<RemoteContract> contracts)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(contracts);

        var items = contracts as RemoteContract[] ?? [.. contracts];
        lock (Gate)
        {
            for (var i = 0; i < Groups.Count; i++)
            {
                if (!ReferenceEquals(Groups[i].Key, groupKey))
                {
                    continue;
                }

                if (Groups[i].Items.AsSpan().SequenceEqual(items))
                {
                    return;
                }

                // Rebuild rejects a set that collides with another assembly's, and it must not leave the
                // rejected group behind: every later Replace rebuilds from the whole list, so a poisoned
                // entry would keep throwing for registrations that have nothing wrong with them.
                var previous = Groups[i];
                Groups[i] = (groupKey, items);
                try
                {
                    Rebuild();
                }
                catch
                {
                    Groups[i] = previous;
                    throw;
                }

                return;
            }

            Groups.Add((groupKey, items));
            try
            {
                Rebuild();
            }
            catch
            {
                Groups.RemoveAt(Groups.Count - 1);
                throw;
            }
        }
    }

    /// <summary>Finds the contract for a message type.</summary>
    /// <param name="messageType">The message's CLR type.</param>
    /// <param name="contract">The contract, when one is registered.</param>
    /// <returns>True when a contract was found.</returns>
    public static bool TryGet(Type messageType, out RemoteContract? contract) =>
        _byType.TryGetValue(messageType, out contract);

    /// <summary>Finds the contract for a wire name — the name in a request path.</summary>
    /// <param name="name">The wire name, exactly as received. Matched ordinally.</param>
    /// <param name="contract">The contract, when one is registered.</param>
    /// <returns>
    ///     True when a contract was found. A false here is what makes an unrecognised name a 404
    ///     <em>before</em> anything from the request body is deserialized.
    /// </returns>
    public static bool TryGet(string name, out RemoteContract? contract)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out contract);
    }

    // Caller holds the gate. A duplicate wire name across two assemblies is a real ambiguity — the
    // endpoint could not tell which handler a request meant — so it throws here rather than letting
    // last-writer-wins decide it silently at module load.
    private static void Rebuild()
    {
        var byType = new Dictionary<Type, RemoteContract>();
        var byName = new Dictionary<string, RemoteContract>(StringComparer.Ordinal);

        foreach (var (_, items) in Groups)
        {
            foreach (var contract in items)
            {
                byType[contract.MessageType] = contract;

                if (byName.TryGetValue(contract.Name, out var existing) &&
                    existing.MessageType != contract.MessageType)
                {
                    throw new InvalidOperationException(
                        $"Two messages claim the wire name '{contract.Name}': '{existing.MessageType}' and "
                        + $"'{contract.MessageType}'. A wire name addresses exactly one message, so give one "
                        + "of them an explicit name.");
                }

                byName[contract.Name] = contract;
            }
        }

        _byType = byType;
        _byName = byName;
        _all = [.. byType.Values];
    }
}
