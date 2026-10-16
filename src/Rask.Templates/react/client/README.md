# React + TypeScript + Vite+

This client is a [Vite+](https://viteplus.dev/guide/) project: `vite.config.ts` configures the dev
server and the build, and the formatter (Oxfmt), the linter (Oxlint) and the test runner (Vitest)
beside them.

| Script           | What it does                                     |
| ---------------- | ------------------------------------------------ |
| `npm run dev`    | The dev server, forwarding `/_rask` to the host. |
| `npm run build`  | Type check, then the production bundle.          |
| `npm run check`  | Format, lint and type-check in one pass.         |
| `npm test`       | The tests, once (`src/*.test.ts`).               |
| `npm run format` | Format every file.                               |

`npm install` also installs a pre-commit hook (`.vite-hooks/pre-commit`) that runs `vp check --fix`
on the files you commit.

The React plugin is [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react),
which uses [Oxc](https://oxc.rs).

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the lint configuration

The `lint` block in `vite.config.ts` is Oxlint's configuration. It is type-aware already, and starts
from the React hook rules; see the [Oxlint rules documentation](https://oxc.rs/docs/guide/usage/linter/rules)
for the full list of rules and categories.
