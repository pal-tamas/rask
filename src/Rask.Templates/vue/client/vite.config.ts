import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig, lazyPlugins } from 'vite-plus'

// One file for the whole toolchain (https://viteplus.dev/guide/): `vp dev` and `vp build` read the
// Vite part, `vp check` the fmt and lint blocks, `vp test` the test block, and the pre-commit hook
// the staged block. There is no ESLint, Prettier or Vitest config beside it to drift from it.
export default defineConfig({
  // Loaded only by the commands that run them, so `vp check` and `vp fmt` start without Vue's.
  plugins: lazyPlugins(() => [vue(), tailwindcss()]),
  server: {
    // In development the browser talks to Vite, and Vite forwards the CQRS calls to the ASP.NET
    // host — so HMR is native and instant, and there is no CORS to configure because the browser
    // only ever sees one origin. In production this proxy is not used at all: the host serves the
    // built bundle and answers /_rask itself.
    proxy: {
      '/_rask': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      // The accounts endpoints, which sit at /api/auth rather than under /_rask. A second entry
      // rather than a wider pattern: this forwards what the host actually answers and leaves the
      // rest of /api to the front end, which may well want routes of its own there.
      '/api/auth': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  // Oxfmt, Prettier-compatible. src/rask/ is generated from the C# contracts on every build, so
  // formatting it is a diff the next build undoes.
  fmt: {
    semi: false,
    singleQuote: true,
    printWidth: 100,
    trailingComma: 'all',
    sortTailwindcss: { stylesheet: './src/style.css' },
    ignorePatterns: ['dist/**', 'src/rask/**', 'package-lock.json'],
  },
  // Oxlint, close to the recommended sets on purpose: a starter that argues with you about style on
  // its first run is a starter people delete the config from. It reads each .vue file's <script>.
  // Type-aware rules, but no type check of its own: TypeScript cannot resolve a .vue import, so the
  // type check is vue-tsc's, which `npm run check` and the build run first.
  lint: {
    ignorePatterns: ['dist/**', 'src/rask/**'],
    plugins: ['vue', 'typescript', 'oxc', 'vitest'],
    jsPlugins: [{ name: 'vite-plus', specifier: 'vite-plus/oxlint-plugin' }],
    rules: {
      'vite-plus/prefer-vite-plus-imports': 'error',
    },
    options: { typeAware: true },
  },
  test: {
    // A starter with Web Push off has no test yet; that is not a failure.
    passWithNoTests: true,
  },
  // What the pre-commit hook (.vite-hooks/pre-commit, installed by `npm install`) runs on the files
  // being committed.
  staged: {
    '*': 'vp check --fix',
  },
})
