// Compiles the Svelte sources the adapter fixture needs, because esbuild cannot: `.svelte` is a template
// language and `$state` in a `.svelte.ts` module is a compiler rune, not a function. In an app this is
// @sveltejs/vite-plugin-svelte's job; here it is the same compiler called directly, so the fixture runs what
// Svelte would have produced from the SHIPPED adapter sources rather than a hand-written stand-in for them.
//
//   node SvelteFixtureCompile.mjs <node_modules root> <out dir> <source>...
//
// Every output lands flat in <out dir> under its source's own file name, so the adapter's relative imports
// (`./RaskHost.svelte`, `./RaskTree.svelte`) resolve between the compiled files exactly as they do between the
// sources. The outputs keep the `.svelte` / `.svelte.ts` names and hold plain JavaScript; the bundle step loads
// them as such.
//
// A rune module arrives with its types ALREADY stripped (the build runs esbuild over it first, which is what the
// Vite plugin does too): Svelte's module compiler reads JavaScript, and node's own type stripping is newer than
// the Node floor the island build asks for.

import {mkdirSync, readFileSync, writeFileSync} from 'node:fs'
import {createRequire} from 'node:module'
import {basename, join} from 'node:path'

const [modules, out, ...sources] = process.argv.slice(2)

// Resolved from the fixture install, which is nowhere above this script on disk.
const {compile, compileModule} = createRequire(join(modules, 'noop.js'))('svelte/compiler')

mkdirSync(out, {recursive: true})

for (const source of sources) {
    const filename = basename(source)
    const text = readFileSync(source, 'utf8')
    const options = {filename, generate: 'client', dev: false}
    const result = filename.endsWith('.svelte') ? compile(text, options) : compileModule(text, options)

    for (const warning of result.warnings) {
        console.warn(`${filename}: ${warning.code}: ${warning.message}`)
    }

    writeFileSync(join(out, filename), result.js.code)
}
