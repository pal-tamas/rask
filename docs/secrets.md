# Secrets

Every app needs values it can't commit — a database password, an SMTP login, an API key. This page is the
whole story for a Rask app: where they live, how they reach the server, and what Rask deliberately does
*not* do with them.

> **The short version.** Put them in a `.env.production` file that git ignores, deploy with
> `rask deploy --env-file .env.production`, and read them through `IConfiguration`. Rask remembers the
> *names* of the variables your app needs and refuses to deploy without them.

## Reading a secret in your app

Secrets arrive as environment variables, which ASP.NET Core's configuration system already reads. Nothing
Rask-specific is involved:

```csharp
// A nested key uses a double underscore in the variable name:
//   ConnectionStrings__App  →  Configuration["ConnectionStrings:App"]
var smtpPassword = builder.Configuration["Smtp:Password"];
```

Environment variables override anything in `appsettings.json`, which is why the same key can hold a
harmless default in the committed file and the real value in production.

## Getting them onto the server

```bash
rask deploy --env "Smtp__Password=…" --env "Stripe__ApiKey=…"   # one-offs
rask deploy --env-file .env.production                          # a file of KEY=VALUE lines
```

`--env-file` takes the format you'd expect — `KEY=VALUE` per line, `#` comments and blank lines ignored.
**Add it to `.gitignore`.** Rask reads it on *your* machine and hands the values to Docker over the SSH
connection; the file itself never leaves your laptop.

Values are passed to `docker run` through a file rather than as `-e KEY=VALUE` arguments, so they don't
appear in your machine's process table where any other local user could read them with `ps`.

## Rask remembers the names, never the values

`.rask/deploy.json` is **committed** — it's how CI knows which host to deploy to — so no secret value is
ever written to it. What *is* recorded is the list of variable names the app was last deployed with:

```jsonc
{
  "host": "deploy@box.example.com",
  "domain": "app.example.com",
  "envKeys": ["Smtp__Password", "Stripe__ApiKey"]   // names only
}
```

That exists to prevent one specific, nasty failure. Without it, a bare `rask deploy` — or the CI workflow,
which passes no `--env` of its own — would start your app *without* its database password. The app boots,
answers its health check, takes traffic, and is quietly misconfigured. So a deploy that doesn't supply a
remembered variable **fails**:

```
This app was last deployed with Smtp__Password, which isn't set now.

Deploying without it would start the app misconfigured, so this is a refusal rather than a warning.
  • pass it again:      rask deploy --env Smtp__Password=…
  • or from a file:     rask deploy --env-file .env.production
  • deploying from CI?  add it to the deploy step in .github/workflows/deploy.yml
  • no longer needed?   remove it from "envKeys" in .rask/deploy.json
```

## Deploying from CI

`rask deploy --github-actions` writes a workflow that needs two repository secrets of its own (an SSH key
and the host's fingerprint). Your **app's** secrets are separate — add them to the deploy step:

```yaml
- name: Deploy
  run: rask deploy --no-setup-host --env "Smtp__Password=${{ secrets.SMTP_PASSWORD }}"
```

If that job starts failing after you deploy a new variable from your own machine, that's the check above
doing its job: add the same `--env` to the workflow.

## What Rask doesn't do

Being explicit, because "secret management" means a lot of different things:

- **No secret store.** Values live in your `.env.production` (or your CI's secret store) and become plain
  environment variables on the container. There's no vault, no encryption at rest, and no rotation.
- **No protection from anyone with Docker access on the box.** `docker inspect` shows a container's
  environment. Anyone in the host's `docker` group is effectively root there anyway — which is why
  [`rask deploy`](deployment.md) creates a dedicated deploy login rather than sharing one.
- **No masking of what your app prints.** If your app logs its own configuration, it logs its secrets.
  Rask masks the values *it* passed when it dumps a failed container's logs, but it can only mask what it
  knows.

For a single-operator product this is usually the right amount of machinery. If you outgrow it, the
environment-variable interface is the standard one — any secret store that can populate env vars at deploy
time drops in without changing your app.

## See also

- [Deployment](deployment.md) — what else `rask deploy` sets on the container.
- [Configuration](configuration.md) — the non-secret half.
- [The `rask` CLI](cli.md#rask-deploy--ship-to-a-single-host-over-ssh) — every deploy option.
