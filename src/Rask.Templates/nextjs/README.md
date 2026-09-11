# Company.RaskServer

A Next.js front end on an ASP.NET host: one project, one container, one port.

## Layout

| | |
|---|---|
| `Company.RaskServer/` | The ASP.NET host: the message records, their handlers, and the JSON endpoint the front end dispatches through. |
| `Company.RaskServer/client/` | The Next.js app, as `create-next-app` scaffolds it, plus the config Rask adjusts. |
| `Company.RaskServer/client/app/rask/` | Generated on every build: your C# contracts as TypeScript, and Rask's browser layer. Gitignored — do not edit. |

## Running it

```bash
rask dev
```

Two processes. The browser talks to Next.js's own dev server on
http://localhost:3000, which proxies `/_rask` back to the host — so hot module replacement is
native and full-speed, with Rask nowhere in its path.

## Calling your C#

```ts
import { rask } from '@rask/client'
import { getGreeting } from '@rask/messages'

const greeting = await rask.dispatch(getGreeting({ name: 'world' }))
```

`greeting` is typed from the C# record. Rename a property there and this stops compiling, which
is the entire point — there is no schema file to keep in sync.

Rask's typed browser APIs are the same import:

```ts
import { getCurrentPosition } from '@rask/browser/geolocation'
```

## In production

Kestrel owns the public port. It serves the framework's content-hashed assets itself, forwards
everything else to the framework's server on loopback, and supervises that process — so ASP.NET
authentication, rate limiting and health checks sit in front of every request.

Map your API **before** `app.UseRaskMeta()`: it registers a fallback, and the symptom of getting
that backwards is an API call answered with a rendered page.
