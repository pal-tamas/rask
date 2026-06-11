<!--
  Thanks for contributing to Rask! 🎉
  Title must follow Conventional Commits: type(scope): subject  (e.g. feat(forms): add X)
  Allowed types: feat, fix, perf, refactor, docs, test, build, ci, chore, revert
  Please do NOT add Co-Authored-By or Generated-with footers.
-->

## Summary

<!-- What does this change and why? Link any related issue: "Closes #123". -->

## Testing

- [ ] Unit tests added/updated (`tests/Rask.*.Tests`)
- [ ] E2E updated for any `samples/` change (`tests/Rask.Examples.E2E.Tests`)
- [ ] `dotnet test --filter "FullyQualifiedName!~Rask.Examples.E2E"` is green

<!-- Paste relevant results. -->

## Benchmarks

<!-- Render/live-runtime hot-path change? Quote the Allocated delta from benchmarks/Rask.Benchmarks. Otherwise: n/a -->

## Checklist

- [ ] `dotnet format` clean; build passes with warnings-as-errors
- [ ] User-facing change → sample + docs/README/NUGET.md/llms.txt/template AGENTS.md updated
- [ ] `CHANGELOG.md` `[Unreleased]` entry added
- [ ] Conventional Commit title; branch will be deleted after merge
