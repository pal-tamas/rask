# Rask.Cqrs.Server

The **server half of Rask.Cqrs remote dispatch**, for [Rask](https://rask.sh/), a full-stack .NET web
framework for teams of any size. It answers the messages a browser app, a TypeScript front end or a meta
framework's server-side renderer sends, and dispatches each to its ordinary handler — no controllers, no
DTO plumbing, no per-message routes.

- **Two endpoints, not one per message.** One GET and one POST resolve a message by name against the
  source-generated allow-list. A query is a GET and can be cached; a command is a POST, so it is **405 on
  GET** and cannot be triggered by a URL, a prefetch or a link scanner. A subscription is served as
  server-sent events, admitted by the notification's watch policy.
- **Fails closed.** Authenticated by default — `[AllowAnonymous]` on the handler is the only way past, and
  `[Authorize]` policies and roles are enforced. An anonymous caller cannot enumerate your messages, both
  verbs require a header no cross-site markup can set, and handler exceptions become RFC 9457
  `problem+json` with no exception text unless you opt in.
- **Files both ways.** Queries answer JSON or a streamed file; commands accept JSON or multipart uploads.
- **TypeScript for free.** Beside Rask.Spa.Hosting or Rask.Meta.Hosting, the build generates the client's
  TypeScript from your C# message records.
- `[LocalOnly]` keeps a message — or a whole interface family — off the wire.

## Install

```bash
dotnet add package Rask.Cqrs.Server
```

## Use

```csharp
// Program.cs
builder.Services.AddRaskCqrsServer();   // calls AddRaskCqrs() for you
```

Then map its endpoints once the app is built, before any SPA or meta framework fallback, so an API call is
never answered with a page. The browser half is **Rask.Cqrs.Client**.

Guide: [CQRS — remote dispatch](https://rask.sh/docs/guides/cqrs) ·
[TypeScript front ends](https://rask.sh/docs/guides/spa)
