# Rask.Cqrs.Client

The **browser half of Rask.Cqrs remote dispatch**, for [Rask](https://rask.sh/), a full-stack .NET web
framework for teams of any size. A page running in WebAssembly reaches its server through the **same
`IDispatcher` call** it would make in-process — no `HttpClient` at the call site, no `/api/*` endpoint to
write, nothing on a message marking it as remote.

- Every request message the client dispatches goes to the server: **queries as GET, commands as POST,
  files as multipart, subscriptions as server-sent events**.
- **A client is a pure client** — a stray client-side handler can never quietly intercept a request.
  Notifications are the deliberate exception: they fan out locally *and* travel.
- **Reflection-free.** The wire codecs are source-generated, so it publishes clean under the WebAssembly
  trimmer.
- The other half is **Rask.Cqrs.Server**; neither references the other, so the browser bundle never
  carries endpoint code.

## Install

```bash
dotnet add package Rask.Cqrs.Client
```

`rask new Shop --template wasm-hosted` scaffolds both halves in one project, with the message records in
`Shared/` and the handlers on the server.

## Use

```csharp
// Shared/Orders.cs — compiled into both the browser app and the server
public sealed record GetOrders(int Page) : IQuery<IReadOnlyList<OrderRow>>;
```

```csharp
// Client/Program.cs
var host = WasmHostBuilder.CreateDefault();
host.Services.AddRaskCqrsClient();
host.Services.AddRaskQuery();   // QueryClient in the browser
await host.RunAsync<App>();
```

```csharp
// A browser page, through Rask.Query: the handler runs on the server.
var orders = QueryClient.Query(new GetOrders(Page));
```

Guide: [CQRS — remote dispatch](https://rask.sh/docs/guides/cqrs)
