// ESLint's flat config. The rules are deliberately close to the recommended sets: a starter that
// argues with you about style on its first run is a starter people delete the config from.
//
// eslint-config-prettier goes LAST and turns off every rule that would fight the formatter, so
// `npm run lint` and `npm run format` cannot disagree about the same line.

import js from '@eslint/js'
import ts from 'typescript-eslint'
import globals from 'globals'
import prettier from 'eslint-config-prettier/flat'
import solid from 'eslint-plugin-solid'

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
  solid.configs['flat/typescript'],
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
  prettier,
)
