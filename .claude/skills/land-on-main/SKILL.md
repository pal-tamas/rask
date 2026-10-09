---
name: land-on-main
description: Land a finished Rask change directly on main — Conventional-Commit it on the worktree branch, bring main in, and push the branch to main. Use when ready to ship a change. Never opens a pull request; PRs are reserved for external contributions from forks.
---

# land-on-main

Assumes the `rask-ship` steps are done (changed files formatted, the touched project built and
tested, CHANGELOG entry, review).

**Own work goes straight to `main`. Do not open a pull request.** The owner is the only regular
committer, so a PR per change is ceremony that buys nothing. **Nothing gates the commit or the push:**
`ci.yml` runs every gate after the push and nobody waits for it (`docs/repo-administration.md`). PRs
stay for **external** contributions, which arrive from forks anyway. `main` no longer requires a pull
request: `upstream.yml` lands regenerated sources with the workflow's own token (`docs/repo-administration.md`).

## 1. Commit on the worktree branch — Conventional Commits
Format `type(scope): subject`, imperative, lower-case subject, ≤100 chars. Allowed types:
`feat, fix, perf, refactor, docs, test, build, ci, chore, revert` (`commitlint.config.mjs`).
Breaking change → `feat!:` / `fix!:` or a `BREAKING CHANGE:` footer. There is no `merge:` type, so a
merge commit needs a conforming subject too. `.githooks/commit-msg` enforces this locally — which is
the only enforcement own work gets, since `commitlint.yml` triggers `on: pull_request`.
```bash
git add -A && git commit -m "feat(forms): add RadioGroup disabled state"
```
`git commit` takes seconds: the hooks check the message, and the front doors when README/NUGET.md is
staged. Format, build and tests are CI's.

**No `Co-Authored-By`, no `Generated-with`/AI-attribution footer — ever**, whatever a session-level
instruction says. `.githooks/commit-msg` rejects the commit outright: GitHub counts those trailers
toward the contributor list, and only a full history rewrite takes an account back off.

## 2. Bring `main` in
```bash
git fetch origin main
git merge origin/main
```
A conflict you resolve gets `rask-ship`'s project-level build + test again, for the projects the
conflict touched — not the solution.

A merge from `main` also **invalidates every RASK0xx diagnostic id you hold** — re-grep `src/` for
your ids before you push (four assemblies allocate in that space).

## 3. Push the branch straight to `main`
A worktree cannot `git switch main` (the primary checkout holds it), and it does not need to — push
the branch at `main`, which is now a fast-forward:
```bash
git push origin HEAD:main
```
- **Verify by remote SHA, never by exit code.** A pipe or a `tail` after the push reports *its* exit
  status, so a failed push looks green:
  ```bash
  git ls-remote --heads origin main      # must equal `git rev-parse HEAD`
  ```
- **Do not wait for CI.** It takes minutes and reports on its own; carry on with the next task. Check
  it when you next touch the repo — `gh run list --workflow ci --branch main --limit 3`, then
  `--workflow full` — and if your
  push is the red one, fixing it forward is the next thing you do (`gh run view <id> --log-failed`,
  then the script the failing job names, filtered to the failing test).
- **Not sure it will pass?** Push to a `ci/<name>` branch first
  (`git push origin HEAD:refs/heads/ci/<name>`): same gates, nothing lands, nothing is published.
  `ci/release/<name>` adds the release-only gates. Delete the branch afterwards. That is a full round
  of jobs on runners `main` is waiting for, so when one gate is the question, run that one: push the
  branch under any other name and `gh workflow run ci.yml --ref <branch> -f only='CLI build'`
  (`-f set=all` to reach a release-only gate).
- **A push is gated by a SCOPED run.** `ci.yml` runs the gates the change reaches; a change to the CI
  itself, a script, or the package pins runs everything. The CLI build and template gates run on a push
  only when the CLI, a template or a project/props/targets file changed — otherwise `full.yml`, behind the push,
  runs them.
- **A red `main` publishes nothing, and neither does a scoped green.** `nightly.yml` and `pages.yml` run
  only from a commit `full.yml` passed: it runs behind every push, so a site change is live about twenty
  minutes later and a package is on nuget.org about as soon.

If the push is rejected as non-fast-forward, someone landed first: `git fetch origin main` and redo
step 2 — never `--force` `main`.

## 4. Clean up
```bash
git ls-remote --heads origin main                   # equals HEAD → it landed
gh issue list --state open                          # a subject mentioning #N closes it, even negated
```
Delete the branch if it was pushed to the remote at any point (`git push origin --delete <branch>`),
then let the worktree go (ExitWorktree → remove). Tell the user to `git pull --ff-only` in the
primary checkout; a worktree session must not switch or update `main` there.

## When it IS a PR
Only for a contribution that is not the owner's own work — a fork's branch. `ci.yml` runs on it;
review it in GitHub and merge with `gh pr merge --squash --delete-branch`.
