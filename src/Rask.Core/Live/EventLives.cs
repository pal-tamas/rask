using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Live;

/// <summary>
///     One live event, as the parts it is made of: each component it runs code for has its own
///     (<see cref="EventLife" />), as a Livewire component's action is a request of its own, and what no component runs
///     has the event's. Made on first use. Guarded by <see cref="EventServices" />' lock.
/// </summary>
internal sealed class EventLives(EventServices services)
{
    private EventLife? _root;
    private Dictionary<Component, EventLife>? _components;

    /// <summary>Whether the event itself is over.</summary>
    internal bool Ended { get; private set; }

    /// <summary>The event's own part, for what no component runs.</summary>
    internal EventLife Root => _root ??= New();

    /// <summary>The event's own part if it has one.</summary>
    internal EventLife? RootIfAny => _root;

    /// <summary>The first scope a part of this event has opened, or null.</summary>
    internal IServiceProvider? Opened
    {
        get
        {
            if (_root?.Scope is { } root)
            {
                return root.ServiceProvider;
            }

            foreach (var life in _components?.Values ?? Enumerable.Empty<EventLife>())
            {
                if (life.Scope is { } scope)
                {
                    return scope.ServiceProvider;
                }
            }

            return null;
        }
    }

    /// <summary><paramref name="owner" />'s part of the event, made on first use.</summary>
    internal EventLife For(Component owner)
    {
        _components ??= new Dictionary<Component, EventLife>(ReferenceEqualityComparer.Instance);
        if (!_components.TryGetValue(owner, out var life))
        {
            life = New();
            _components.Add(owner, life);
        }

        return life;
    }

    /// <summary>
    ///     Ends the event: the scopes of the parts nothing needs any more go into <paramref name="done" />, the parts
    ///     still working into <paramref name="running" />.
    /// </summary>
    internal void End(List<AsyncServiceScope> done, HashSet<EventLife> running)
    {
        Ended = true;
        foreach (var life in Parts())
        {
            life.Ended = true;
            if (!life.Done)
            {
                running.Add(life);
            }
            else if (life.Scope is { } scope)
            {
                done.Add(scope);
                life.Scope = null;
            }
        }
    }

    /// <summary>Every scope a part has open, taken for the session's end.</summary>
    internal void TakeAll(List<AsyncServiceScope> into)
    {
        foreach (var life in Parts())
        {
            EventServices.Take(life, into);
        }
    }

    private IEnumerable<EventLife> Parts()
    {
        if (_root is not null)
        {
            yield return _root;
        }

        foreach (var life in _components?.Values ?? Enumerable.Empty<EventLife>())
        {
            yield return life;
        }
    }

    private EventLife New() => new(services) { Ended = Ended };
}
