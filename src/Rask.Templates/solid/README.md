# Company.RaskServer

A Solid front end on an ASP.NET host, talking to it over Rask's CQRS wire.

## Layout

| | |
|---|---|
| `Company.RaskServer/` | The ASP.NET host: the message records, their handlers, and the JSON endpoint the client dispatches through. |
| `Company.RaskServer/client/` | The Solid app, as `create-vite` scaffolds it, plus four files Rask overlays. |
| `Company.RaskServer/client/src/rask/` | Generated on every build from the server's contracts. Gitignored — do not edit. |

## Running it

```bash
rask dev
```

The browser talks to Vite, which forwards `/_rask` to the ASP.NET host. In production the host
serves the built bundle itself and answers `/_rask` directly, so there is no proxy in the way.

## Adding a message

Add a record to `Features/Hello/Messages.cs` and a handler beside it. The next build writes its
TypeScript into `src/rask/`, and the front end imports a factory with the payload and result types
already attached:

```ts
const order = await rask.dispatch(getOrder({ id }))
```

A `DateTimeOffset` arrives as a real `Date`. A `DateOnly` deliberately does not — it stays a
`YYYY-MM-DD` string, because `new Date("2026-08-25")` is UTC midnight and would render as the
previous day for anyone west of UTC.

## The client stays TypeScript

The contracts are generated as `.ts`, and the host checks that the client can compile them: a
client with no `tsconfig.json` fails the build with `RASKSPA004`. That is the whole guarantee
— a renamed C# property becomes a front-end compile error rather than a wrong payload — so
dropping to JavaScript would keep the imports working and quietly remove every check behind
them.
