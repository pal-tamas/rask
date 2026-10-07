// ESLint's flat config. The rules are deliberately close to the recommended sets: a starter that
// argues with you about style on its first run is a starter people delete the config from.
//
// eslint-config-prettier goes LAST and turns off every rule that would fight the formatter, so
// `npm run lint` and `npm run format` cannot disagree about the same line.

import js from '@eslint/js'
import ts from 'typescript-eslint'
import globals from 'globals'
import prettier from 'eslint-config-prettier/flat'
import vue from 'eslint-plugin-vue'

export default ts.config(
  { ignores: ['dist/**', 'src/rask/**', 'node_modules/**'] },
  js.configs.recommended,
  ...ts.configs.recommended,
  ...vue.configs['flat/recommended'],
  { files: ['**/*.vue'], languageOptions: { parserOptions: { parser: ts.parser } } },
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
      // The starter's own components are App.vue and Auth.vue: single words, on purpose.
      'vue/multi-word-component-names': 'off',
    },
  },
  prettier,
)
