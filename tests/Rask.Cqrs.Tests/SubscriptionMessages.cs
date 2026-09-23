namespace Rask.Cqrs.Tests;

// ---- Watched by type: every subscriber gets every one ----
public sealed record Chimed(int Number) : INotification;

public sealed record Whistled(int Number) : INotification;

// ---- Watched through a record, which a policy guards ----
public enum Colour
{
    Red,
    Green,
}

public sealed record DoorOpened(Guid DoorId, string Note = "") : INotification;

public sealed record DoorPainted(Colour Colour) : INotification;

public sealed record WatchDoor(Guid DoorId) : ISubscription<DoorOpened>
{
    public bool Matches(DoorOpened opened) => opened.DoorId == DoorId;
}

// A subscription that filters on something other than an id, and by more than equality.
public sealed record WatchPaint(Colour Colour) : ISubscription<DoorPainted>
{
    public bool Matches(DoorPainted painted) => painted.Colour == Colour;
}

/// <summary>The doors the policy lets through, set per test.</summary>
public sealed class Keyholder
{
    public HashSet<Guid> Allowed { get; } = [];
}

// Registered by the generator, like a handler. One class may admit several subscriptions.
public sealed class DoorPolicy(Keyholder keys) : IWatchPolicy<WatchDoor>, IWatchPolicy<WatchPaint>
{
    public Task<bool> CanWatchAsync(WatchDoor subscription, CancellationToken cancellationToken) =>
        Task.FromResult(keys.Allowed.Contains(subscription.DoorId));

    public Task<bool> CanWatchAsync(WatchPaint subscription, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

// ---- A subscription nothing guards — so nobody may open it ----
public sealed record VaultOpened(int VaultId) : INotification;

public sealed record WatchVault(int VaultId) : ISubscription<VaultOpened>
{
    public bool Matches(VaultOpened opened) => opened.VaultId == VaultId;
}
