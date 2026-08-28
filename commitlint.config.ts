// Conventional Commits, enforced in CI by .github/workflows/commitlint.yml and locally by
// .githooks/commit-msg. See https://www.conventionalcommits.org/.
//
// TypeScript, like everything else authored here. wagoid/commitlint-github-action@v6 loads it: the
// action's image carries @commitlint/load, which registers cosmiconfig-typescript-loader for `.ts`,
// and that loader is jiti-only — no ts-node, no tsconfig lookup, nothing for this repo to provide.
//
// `.ts`, not `.mts`: @commitlint/load 19.x maps `.ts` and `.cts` and nothing else. The docs list
// `.mts` because they describe a later major.
//
// The type import is `import type`, so jiti elides it. The action does not install this repo's
// dependencies, so a value-position import of @commitlint/types would throw — `config-conventional`
// resolves only because it ships in the action's own image.
import type {UserConfig} from "@commitlint/types";

const config: UserConfig = {
    extends: ["@commitlint/config-conventional"],
    rules: {
        "type-enum": [
            2,
            "always",
            ["feat", "fix", "perf", "refactor", "docs", "test", "build", "ci", "chore", "revert"],
        ],
        "subject-case": [2, "never", ["upper-case", "pascal-case", "start-case"]],
        "header-max-length": [2, "always", 100],
        // Allow long body/footer lines (URLs, issue refs, wrapped prose) — only the header is bounded.
        "body-max-line-length": [0, "always", Infinity],
        "footer-max-line-length": [0, "always", Infinity],
    },
};

export default config;
