# Rask.Redis

**A Redis backplane for Rask's `IBroadcast`.** Behind a load balancer, each instance of an ASP.NET Core app holds its
own visitors' live pages, so a broadcast published on one instance reaches only those. `AddRaskRedisBackplane()`
carries it over Redis pub/sub, and a publish on any instance re-renders the subscribed pages on every instance.

## Install

```bash
dotnet add package Rask.Redis
```

## Use

```csharp
builder.Services.AddRaskRedisBackplane();
```

The connection string is `Rask:ConnectionStrings:Redis` in configuration (`Rask__ConnectionStrings__Redis` in the
environment); a missing one stops the start, naming that key. If the app already registers an
`IConnectionMultiplexer`, the backplane uses it.

Only topics declared with a source-generated JSON contract cross; every other topic stays in its process:

```csharp
public static readonly Topic<OrderPlaced> Orders = new("orders", AppJson.Default.OrderPlaced);

[JsonSerializable(typeof(OrderPlaced))]
public sealed partial class AppJson : JsonSerializerContext;
```

A publish is delivered on its own instance first and then sent to the others; an instance never delivers its own
message twice. Delivery is at most once, as it is inside one process.

Two apps sharing one Redis server give each its own channel prefix in `Rask:Redis:ChannelPrefix` (default
`rask:broadcast:`).

Full guide: https://rask.sh/docs/guides/broadcast/
