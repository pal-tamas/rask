namespace Rask.Api.Client;

/// <summary>
///     Every generated API client in the process, keyed by the type an app injects. Populated at module
///     load by code the Rask.Api source generator emits, then drained by <c>AddRaskApiClient()</c>. This
///     type is public only so generated code can call into it; you do not use it directly.
/// </summary>
public static class ApiClientRegistry
{
    private static readonly object Gate = new();

    // One entry per contributing assembly, keyed by that assembly's generated registry type, so a
    // hot-reload re-run swaps that assembly's contribution instead of merging into it — the same shape,
    // and the same reason, as Rask.Cqrs' registries.
    private static readonly List<(object Key, ApiClientRegistration[] Items)> Groups = [];

    private static volatile ApiClientRegistration[] _all = [];

    /// <summary>Every registered client, in no meaningful order.</summary>
    public static IReadOnlyList<ApiClientRegistration> All => _all;

    /// <summary>
    ///     Installs <paramref name="clients" /> as the complete set owned by <paramref name="groupKey" />,
    ///     replacing anything that key contributed before.
    /// </summary>
    /// <param name="groupKey">The contributing assembly's generated registry type.</param>
    /// <param name="clients">That assembly's complete client set.</param>
    /// <exception cref="InvalidOperationException">
    ///     Two assemblies claim the same client type, which would make which one an app injects depend on
    ///     module load order.
    /// </exception>
    public static void Replace(object groupKey, IEnumerable<ApiClientRegistration> clients)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        ArgumentNullException.ThrowIfNull(clients);

        var items = clients as ApiClientRegistration[] ?? [.. clients];

        lock (Gate)
        {
            for (var i = 0; i < Groups.Count; i++)
            {
                if (!ReferenceEquals(Groups[i].Key, groupKey))
                {
                    continue;
                }

                var previous = Groups[i];
                Groups[i] = (groupKey, items);

                try
                {
                    Rebuild();
                }
                catch
                {
                    // A rejected set must not be left behind: every later Replace rebuilds from the whole
                    // list, so a poisoned entry would keep throwing for registrations with nothing wrong
                    // with them.
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

    // Caller holds the gate.
    private static void Rebuild()
    {
        var byType = new Dictionary<Type, ApiClientRegistration>();

        foreach (var (_, items) in Groups)
        {
            foreach (var client in items)
            {
                if (byType.TryGetValue(client.ClientType, out var existing) &&
                    !ReferenceEquals(existing.Factory, client.Factory))
                {
                    throw new InvalidOperationException(
                        $"Two assemblies register an API client for '{client.ClientType}'. Which one an "
                        + "app injects would depend on module load order, so one of them has to go.");
                }

                byType[client.ClientType] = client;
            }
        }

        _all = [.. byType.Values];
    }
}

/// <summary>
///     One generated API client: the type an app injects, and how to build it.
/// </summary>
/// <param name="ClientType">The client's CLR type.</param>
/// <param name="Factory">Builds the client over a configured <see cref="HttpClient" />.</param>
public readonly record struct ApiClientRegistration(Type ClientType, Func<HttpClient, ApiClientOptions, object> Factory);
