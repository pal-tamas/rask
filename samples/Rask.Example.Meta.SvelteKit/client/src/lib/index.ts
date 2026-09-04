// $lib is SvelteKit's own alias, generated into the tsconfig it writes. Rask's `@rask` alias is
// generated the same way, from kit.alias — and an earlier attempt to add `@rask` as a hand-written
// tsconfig `paths` entry silently DISPLACED this one, so an import of $lib stopped resolving in code
// nobody had touched. Both are used on this page so that regression cannot come back quietly.
export const greetingLabel = 'From C#, during the server render'
