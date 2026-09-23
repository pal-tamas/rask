using Microsoft.AspNetCore.Authorization;

namespace Rask.Cqrs.Transport.Tests;

// The notifications a client subscribes to over the event stream. None has a handler: publishing reaches subscribers
// whether or not anything handles it.

/// <summary>Open to any signed-in caller, because the record says so.</summary>
[Authorize]
public sealed record Announced(string Text) : INotification;

/// <summary>Open to admins only.</summary>
[Authorize(Roles = "admin")]
public sealed record AdminNotice(string Text) : INotification;

/// <summary>Open to signed-out visitors too.</summary>
[AllowAnonymous]
public sealed record PublicNotice(string Text) : INotification;

/// <summary>Declares nothing, so nothing outside the server may subscribe to it.</summary>
public sealed record Undeclared(string Text) : INotification;

public sealed class Room;

/// <summary>Scoped: reaches only the subscribers watching its room, each admitted by the policy below.</summary>
public sealed record RoomMessage([For<Room>] int Room, string Text) : INotification;

/// <summary>Room 1 is open; every other room is closed.</summary>
public sealed class RoomPolicy : IWatchPolicy<Room>
{
    public Task<bool> CanWatchAsync(object key, CancellationToken cancellationToken) => Task.FromResult(key is 1);
}
