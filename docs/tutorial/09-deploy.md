# Chapter 9 — Deploy to one box

> **Goal:** ship Shop to a single server, behind automatic HTTPS, with one command.
> **You'll run:** `rask deploy --host … --domain …`

You have the whole product — UI, data, auth, jobs, mail, cache, events, production SQLite with off-box
backup — in one project. Now put it on the internet. `rask deploy` builds your Docker image **on the server
over SSH** and runs it; with a domain it fronts the app with a shared [Caddy](https://caddyserver.com) proxy
for automatic Let's Encrypt HTTPS, swapping the new container in only after a `/health` check passes.

## 1. What you need

- A Linux box you can SSH into (any cheap VPS), and a DNS **A record** for your domain pointing at it.
- The `Dockerfile` from Chapter 1's `--docker` flag (already in the project). No Dockerfile? Add one with
  `rask new … --docker` next time, or write a standard .NET one — `rask deploy` just builds it.

## 2. First deploy (bare box → live HTTPS)

Point at the box as `root` (or any sudo user) and give your domain. On a fresh box, `rask deploy` also sets
it up — installs Docker, creates a non-root `deploy` user, configures the firewall, and hardens SSH —
before building and running:

```bash
rask deploy --host root@your-box.example.com --domain shop.example.com
```

When it finishes, `https://shop.example.com` is live with a valid certificate. `rask deploy` remembers the
host and settings in `.rask/deploy.json`, so from then on a re-ship is just:

```bash
rask deploy
```

Each deploy builds the new image, starts it alongside the old one, waits for `/health` to go green, then
switches traffic over — so there's no downtime, and a broken build never replaces a working one.

> **No domain yet?** `rask deploy --host deploy@your-box --port 8080` runs it on a published port over plain
> HTTP — handy for a quick internal test. Add `--domain` when you're ready for HTTPS.

## 3. Deploy from CI (optional)

To ship on every push instead of from your laptop:

```bash
rask deploy --github-actions
```

That writes `.github/workflows/deploy.yml` and prints the repository secrets to add. Push to your default
branch and the workflow runs the same deploy.

## 4. Where your data lives

Two layers keep the SQLite database safe, and you should understand both:

- **Across redeploys — a persistent volume.** Every `rask deploy` runs a *fresh* container, so the database
  can't live inside it. `rask deploy` mounts a per-app Docker volume and points the app at it
  (`ConnectionStrings:App` → `Data Source=/data/app.db`); the volume — and your data — persists across
  container replacements. The old container is stopped gracefully (SIGTERM) before removal, so in-flight
  writes are checkpointed first rather than killed. *(Bringing your own Dockerfile? Give the runtime a
  writable `/data`: `RUN mkdir -p /data && chown $APP_UID:$APP_UID /data`. The one `rask new --docker`
  scaffolds already does this.)*
- **Off the box — Litestream.** The volume survives redeploys; Litestream (Chapter 8) survives losing the
  *box*, streaming the database off-site continuously so you can restore onto a new machine.

Litestream needs its replica credentials in the container. A bare `rask deploy` re-run doesn't remember
one-shot `--env` values — they're secrets, so only the `--env-file` **path** is persisted — so pass them via a
file, and every deploy will have them:

```bash
rask deploy --env-file .env.production   # AWS_ACCESS_KEY_ID=… / AWS_SECRET_ACCESS_KEY=… inside
```

## Verify

- `https://shop.example.com` serves the app over HTTPS with a valid certificate.
- `/products` and `/orders` work; signing in gates the edit pages; placing an order fires the job → email →
  outbox chain.
- A second `rask deploy` swaps in a new build with no downtime **and your data is still there** (the volume
  persists across the container swap); a deliberately broken build fails the `/health` check and leaves the
  running app untouched.

## You shipped it

Start to finish, one person built and deployed a real product — catalog, orders, login, background jobs,
transactional email, caching, durable domain events, a production database with continuous backup — as **one
C# codebase on one server**, with no PaaS, no broker, and no second language. That's the
[One Person Framework](../one-person-framework.md).

Where to go next:

- **Harden auth** → swap the demo credential store for a real one — [authentication](../authentication.md).
- **Go deeper on any pillar** → [jobs](../jobs.md) · [mail](../mail.md) · [cache](../cache.md) ·
  [outbox](../outbox.md) · [CQRS](../cqrs.md) · [production SQLite](../sqlite.md).
- **Add reach** → the same components as an installable [PWA](../pwa.md) or a [native](../native.md) app.
- **See the roadmap** → what's shipped and what's next — [roadmap](../roadmap.md).

**Learn more:** [deployment](../deployment.md) · [the `rask` CLI](../cli.md)
