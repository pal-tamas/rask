# Repository administration

One-time GitHub settings that back the committed governance files. These are repo **settings**
(not files), so apply them in the GitHub UI or with `gh`. Goal: **contributions are open** —
anyone can open issues and PRs — but **only the owner (@pal-tamas) can merge**, for now.

## Enable community features
- **Settings → General → Features:** enable **Issues** and **Discussions**.
- **Settings → General → Pull Requests:** enable **Automatically delete head branches**, and
  allow **Squash merging** (Conventional-Commit titles → clean history).

## Protect `main` (only the owner merges)
**Settings → Branches → Add branch ruleset** (or classic protection) for `main`:
- ❌ Require a pull request before merging — **off**. `upstream.yml` lands what it regenerated with
  the workflow's own token, which is not an admin and cannot bypass the rule; and the rule held
  nobody else, because only the owner has write access and an outside contribution arrives from a
  fork as a pull request whatever this says. Turn it back on and the daily run stops at `land`.
- ✅ Require status checks to pass: see below — in practice this list stays **empty**.
- ✅ Require branches to be up to date before merging.
- ✅ Block force pushes and deletions.
- Write access stays with @pal-tamas alone: that, not a branch rule, is what "only the owner merges" rests on.

### Why the required-checks list is empty

**CI runs the gates after the push, and blocks nothing.** The owner's own work lands on `main` by a
direct push, so a required check would have nothing to hold: by the time `ci.yml` runs, the commit is
already there. It runs the format check, the warnings-as-errors build, the unit suite, the browser
E2E journeys, the CLI build and the template gate on every push to `main` and on every pull request,
and a red `main` is fixed forward. See [development-workflow.md](development-workflow.md#ci).

**What a red run does stop is publishing.** `pages.yml` and `nightly.yml` trigger on `full`'s (the whole run's) completion,
and both publish only from a commit it passed; `release.yml` runs every gate, the release-only
ones included, before it packs. That is in the workflows, not in a branch setting, so it needs
nothing configured here.

`commitlint.yml` covers the one path a local hook cannot reach: an **external** contribution, where
the **PR title** becomes the squash commit on `main` and no `commit-msg` hook ever sees it. It
triggers `on: pull_request` only, so it says nothing about the maintainer's own work — that is linted
by `.githooks/commit-msg` instead (which also refuses AI-attribution trailers).

An unrequired check on a pull request is advisory: a red `ci` run does not disable the merge button
([#919](https://github.com/pal-tamas/rask/issues/919) is a gate that rode red through three merges
that way). Read the run before merging an external PR.

If a required check is ever added, read the live state back — `contexts: []` means nothing is
enforced, whatever this file claims:

```bash
gh api repos/pal-tamas/rask/branches/main/protection \
  --jq '{checks: .required_status_checks.checks, strict: .required_status_checks.strict,
         enforce_admins: .enforce_admins.enabled}'
```

Add one with the **required-status-checks sub-resource**, never `PUT …/protection` — the latter
replaces the whole object, so a partial body silently drops `enforce_admins`, conversation resolution
and the force-push/deletion blocks:

```bash
gh api -X PATCH repos/pal-tamas/rask/branches/main/protection/required_status_checks \
  --input - <<'JSON'
{"strict": true, "checks": [{"context": "commitlint"}]}
JSON
```

Only ever require a check that actually runs on every PR. A required check that is skipped — by a path
filter, or because its workflow was deleted — blocks the branch for ever with no way to satisfy it.
The gate jobs are named by `gates.yml`'s matrix (`build`, `unit 1/2`, `format src`, `browser E2E 1/5`, …); requiring one
means keeping that name in step with the list there.

The reviews-and-restrictions half, set once (example):
```bash
gh api -X PUT repos/pal-tamas/rask/branches/main/protection \
  -H "Accept: application/vnd.github+json" \
  -f required_pull_request_reviews.require_code_owner_reviews=true \
  -F required_pull_request_reviews.required_approving_review_count=1 \
  -F enforce_admins=true \
  -F required_status_checks.strict=true \
  -F restrictions.users[]=pal-tamas -F 'restrictions.teams[]' -F 'restrictions.apps[]'
```

## Secrets used by workflows
- `NUGET_API_KEY` — nuget.org push (used by `release.yml` and `nightly.yml`).
- `GITHUB_TOKEN` — provided automatically (GitHub Packages + releases).
