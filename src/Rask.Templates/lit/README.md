# Company.RaskServer

A Lit front end on an ASP.NET host, talking to it over Rask's CQRS wire.

## Layout

| | |
|---|---|
| `Features/` | The host's half: the message records and their handlers. |
| `client/` | The Lit app — Vite, TypeScript, Tailwind. |
| `client/src/rask/` | Generated on every build from the host's messages. Gitignored — do not edit. |

## Running it

```bash
rask dev
```

The browser talks to Vite, which forwards `/_rask` and `/api/auth` to the ASP.NET host. In production
the host serves the built bundle itself and answers both directly, so there is no proxy in the way.

## Adding a message

Add a record to `Features/Hello/Messages.cs` and a handler beside it. The next build writes its
TypeScript into `client/src/rask/`, and the front end imports a factory with the payload and result
types already attached:

```ts
const order = await rask.dispatch(getOrder({ id }))
```

A `DateTimeOffset` arrives as a real `Date`. A `DateOnly` deliberately does not — it stays a
`YYYY-MM-DD` string, because `new Date("2026-08-25")` is UTC midnight and would render as the
previous day for anyone west of UTC.

## The client stays TypeScript

The typed client is generated as `.ts`, and the build checks that the client can compile it: a
client with no `tsconfig.json` fails with `RASKSPA004`. A renamed C# property is then a front-end
compile error rather than a wrong payload.
