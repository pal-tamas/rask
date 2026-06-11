---
name: open-pr
description: Open a GitHub PR for the current Rask branch following project conventions. Use when ready to submit a change. Branches off main if needed, writes a structured PR body (summary, testing, benchmarks, changelog), and omits all AI attribution footers.
---

# open-pr

Assumes `rask-ship` steps 1–6 are green (format, warnings-as-errors build, tests, benchmarks if
hotpath, CHANGELOG entry, review).

## 1. Branch
Never commit straight to `main`. If on `main`, branch first:
```bash
git rev-parse --abbrev-ref HEAD            # if "main":
git switch -c <type>/<short-desc>          # e.g. feat/dialog-tag, fix/diff-rotation
```

## 2. Commit — Conventional Commits (enforced by commitlint)
Format `type(scope): subject`, imperative, lower-case subject, ≤100 chars. Allowed types:
`feat, fix, perf, refactor, docs, test, build, ci, chore, revert` (`commitlint.config.mjs`,
checked in CI by `.github/workflows/commitlint.yml`). Breaking change → `feat!:` / `fix!:` or a
`BREAKING CHANGE:` footer. **No `Co-Authored-By`, no `Generated-with`/AI-attribution footers** — ever.
```bash
git add -A && git commit -m "feat(forms): add RadioGroup disabled state"
```

## 3. PR
```bash
git push -u origin HEAD
gh pr create --base main --title "<title>" --body-file <(cat .claude/skills/open-pr/templates/pr-body.md)
```
Fill the body from `templates/pr-body.md`: **Summary** · **Testing** (unit + which E2E host, with
results) · **Benchmarks** (Allocated delta if framework hotpath, else "n/a") · **CHANGELOG**
(link the `[Unreleased]` entry). CI runs the unit gate + sharded E2E matrix + commitlint; a
release is cut by tagging `vX.Y.Z` (MinVer), which triggers `release.yml`.

## 4. Merge — delete the branch
Always delete the branch after merge (squash):
```bash
gh pr merge --squash --delete-branch
git switch main && git pull && git remote prune origin
```
(Set repo default: Settings → "Automatically delete head branches".)
