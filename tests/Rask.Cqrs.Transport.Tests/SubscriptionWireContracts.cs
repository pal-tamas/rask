using Microsoft.AspNetCore.Authorization;

namespace Rask.Cqrs.Transport.Tests;

// The events a client subscribes to over the event stream. None has a handler: publishing reaches subscribers
// whether or not anything handles it.

/// <summary>Open to any signed-in caller, because the record says so.</summary>
[Authorize]
public sealed record Announced(string Text) : IEvent;

/// <summary>Open to admins only.</summary>
[Authorize(Roles = "admin")]
public sealed record AdminNotice(string Text) : IEvent;

/// <summary>Open to signed-out visitors too.</summary>
[AllowAnonymous]
public sealed record PublicNotice(string Text) : IEvent;

/// <summary>Declares nothing, so nothing outside the server may subscribe to it.</summary>
public sealed record Undeclared(string Text) : IEvent;

public sealed record RoomMessage(int Room, string Text) : IEvent;

/// <summary>One room's messages, asked for by the record and admitted by the policy below.</summary>
public sealed record WatchRoom(int Room) : ISubscription<RoomMessage>
{
    public bool Matches(RoomMessage message) => message.Room == Room;
}

/// <summary>Room 1 is open; every other room is closed.</summary>
public sealed class RoomPolicy : IWatchPolicy<WatchRoom>
{
    public Task<bool> CanWatch(WatchRoom subscription, CancellationToken cancellationToken) =>
        Task.FromResult(subscription.Room == 1);
}
