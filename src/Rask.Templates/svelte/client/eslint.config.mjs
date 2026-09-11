// ESLint's flat config. The rules are deliberately close to the recommended sets: a starter that
// argues with you about style on its first run is a starter people delete the config from.
//
// eslint-config-prettier goes LAST and turns off every rule that would fight the formatter, so
// `npm run lint` and `npm run format` cannot disagree about the same line.

import js from '@eslint/js'
import ts from 'typescript-eslint'
import globals from 'globals'
import prettier from 'eslint-config-prettier/flat'
import svelte from 'eslint-plugin-svelte'

export default ts.config(
  {
    ignores: [
      'dist/**',
      'build/**',
      '.output/**',
      '.next/**',
      '.nuxt/**',
      '.vinxi/**',
      'src/rask/**',
      'app/rask/**',
      'node_modules/**',
      '.svelte-kit/**',
      '.angular/**',
    ],
  },
  js.configs.recommended,
  ...ts.configs.recommended,
  ...svelte.configs.recommended,
  { files: ['**/*.svelte'], languageOptions: { parserOptions: { parser: ts.parser } } },
  {
    languageOptions: {
      globals: { ...globals.browser },
    },
  },
  {
    files: ['**/*sw.js', '**/service-worker.js'],
    languageOptions: {
      globals: { ...globals.serviceworker },
    },
  },
  {
    rules: {
      // Reading a rune to declare a dependency IS the Svelte 5 idiom, and a bare `name;`
      // inside $effect is what it looks like — an expression with no result, on purpose.
      '@typescript-eslint/no-unused-expressions': 'off',
      // resolve() exists for apps served under a base path. This one is not, and requiring
      // it would put an import in front of every link in a starter that has no base.
      'svelte/no-navigation-without-resolve': 'off',
    },
  },
  prettier,
)
