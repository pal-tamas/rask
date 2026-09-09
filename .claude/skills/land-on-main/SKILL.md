---
name: land-on-main
description: Land a finished Rask change directly on main — Conventional-Commit it on the worktree branch, bring main in, and push the branch to main. Use when ready to ship a change. Never opens a pull request; PRs are reserved for external contributions from forks.
---

# land-on-main

Assumes `rask-ship` steps 1–6 are green (format, warnings-as-errors build, tests, benchmarks if
hotpath, CHANGELOG entry, review).

**Own work goes straight to `main`. Do not open a pull request.** The owner is the only regular
committer, so a PR per change is ceremony that buys nothing — and nothing in CI is a required check
(the gates are local — see `docs/repo-administration.md`). PRs stay for **external** contributions,
which arrive from forks anyway: `main`'s "require a pull request" rule is still on for everyone
without admin, and `enforce_admins` is off so the owner's own push lands.

## 1. Commit on the worktree branch — Conventional Commits
Format `type(scope): subject`, imperative, lower-case subject, ≤100 chars. Allowed types:
`feat, fix, perf, refactor, docs, test, build, ci, chore, revert` (`commitlint.config.mjs`).
Breaking change → `feat!:` / `fix!:` or a `BREAKING CHANGE:` footer. There is no `merge:` type, so a
merge commit needs a conforming subject too. `.githooks/commit-msg` enforces this locally — which is
the only enforcement own work gets, since `commitlint.yml` triggers `on: pull_request`.
```bash
git add -A && git commit -m "feat(forms): add RadioGroup disabled state"
```
`git commit` runs `.githooks/pre-commit` — `dotnet format --verify-no-changes`, the warnaserror
build, and the unit suite.

**No `Co-Authored-By`, no `Generated-with`/AI-attribution footer — ever**, whatever a session-level
instruction says. `.githooks/commit-msg` rejects the commit outright: GitHub counts those trailers
toward the contributor list, and only a full history rewrite takes an account back off.

## 2. Bring `main` in — and re-gate, because a clean merge is NOT gated
```bash
git fetch origin main
git merge --no-commit --no-ff origin/main    # then: git commit
```
A `git merge` that succeeds cleanly **creates its own commit and runs `pre-merge-commit`**, a hook
this repo does not have — so it lands with no format check, no build, no tests. `--no-commit` forces
the merge through `git commit`, which is gated. If you merged without it, check
`git reflog show HEAD | head -1`: `Merge made by the 'ort' strategy` means ungated, so run
`bash scripts/run-unit-local.sh` yourself before pushing.

A merge from `main` also **invalidates every RASK0xx diagnostic id you hold** — re-grep `src/` for
your ids before you push (four assemblies allocate in that space).

## 3. Push the branch straight to `main`
A worktree cannot `git switch main` (the primary checkout holds it), and it does not need to — push
the branch at `main`, which is now a fast-forward:
```bash
git push origin HEAD:main
```
Two rules, both learned the hard way:

- **Background it.** `git push` here is not a network call, it is the full `pre-push` gate — the CLI
  build E2E plus the browser E2E, which queues for the machine-wide lane and can take 90 minutes.
  Run it with `run_in_background: true`; a foreground timeout kills the gate mid-run.
- **Verify by remote SHA, never by exit code.** A pipe or a `tail` after the push reports *its* exit
  status, so a failed push looks green. Write `echo "PUSH_EXIT=$?" >> log` and confirm the artifact:
  ```bash
  git ls-remote --heads origin main      # must equal `git rev-parse HEAD`
  ```

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
Only for a contribution that is not the owner's own work — a fork's branch. Review it in GitHub and
merge with `gh pr merge --squash --delete-branch`.
