namespace Rask.Cqrs.Tests;

// ---- Unscoped: every subscriber gets every one ----
public sealed record Chimed(int Number) : INotification;

public sealed record Whistled(int Number) : INotification;

// ---- Scoped to a Door, which a policy guards ----
public sealed class Door;

public enum Colour
{
    Red,
    Green,
}

public sealed record DoorOpened([For<Door>] Guid DoorId) : INotification;

public sealed record DoorTagged([For<Door>] string Tag) : INotification;

public sealed record DoorPainted([For<Door>] Colour Colour) : INotification;

// The attribute on a property rather than a positional parameter.
public sealed class DoorKnocked : INotification
{
    [For<Door>]
    public long DoorNumber { get; init; }
}

/// <summary>The keys the door policy lets through, set per test.</summary>
public sealed class Keyholder
{
    public HashSet<object> Allowed { get; } = [];
}

// Registered by the generator, like a handler.
public sealed class DoorPolicy(Keyholder keys) : IWatchPolicy<Door>
{
    public Task<bool> CanWatchAsync(object key, CancellationToken cancellationToken) =>
        Task.FromResult(keys.Allowed.Contains(key));
}

// ---- Scoped to a Vault, which nothing guards — so nobody may watch it ----
public sealed class Vault;

public sealed record VaultOpened([For<Vault>] int VaultId) : INotification;
