// rask-external Svelte adapter, vendored from Rask.External.
//
// A `.svelte.ts` module rather than a plain `.ts` one, and that extension is load-bearing: `$state`
// is a COMPILER rune, not a function, so it does not exist in a file the Svelte plugin does not
// compile. Without it the only way to show new props would be to remount, which throws away the
// component's own state on every C# re-render — the one thing the diff boundary exists to prevent.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createRawSnippet, mount, unmount, type Component } from 'svelte'
import type { ExternalAdapter, ExternalProps, ExternalSlots } from './adapter'

/** What `mount` hands back: the instance to unmount, and the reactive object updates are written into. */
interface SvelteHandle {
  instance: Record<string, unknown>
  state: ExternalProps
}

/**
 * Wraps a Svelte component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Svelte component with no Rask import in it.
 */
export function svelteComponent(Component: Component<Record<string, unknown>>): ExternalAdapter<SvelteHandle> {
  // Per island, not per module. The snippet props are placed once at mount and must survive every
  // later update; a set shared across the page would let one island's slot name protect another's.
  const slotKeys = new Set<string>()

  return {
    mount(element, props, slots) {
      const snippets = adopt(slots ?? {})
      for (const key of Object.keys(snippets)) slotKeys.add(key)

      // Handed to `mount` by reference and then mutated in place for the life of the island. Svelte
      // tracks the proxy's properties, so assigning to one is what reaches the component — replacing
      // this object instead would leave the component reading the original forever.
      const state: ExternalProps = $state({ ...props, ...snippets })

      const instance = mount(Component, { target: element, props: state }) as Record<string, unknown>
      return { instance, state }
    },

    update(handle, props) {
      // Deleted first, then assigned. An unwired callback omits its key entirely, so without the
      // delete a callback cleared in C# would keep firing the stale one. Slot keys are left alone —
      // they are snippets placed at mount, not props, and dropping one would blank the slot.
      for (const key of Object.keys(handle.state)) {
        if (!(key in props) && !slotKeys.has(key)) {
          delete handle.state[key]
        }
      }

      Object.assign(handle.state, props)
      return handle
    },

    unmount(handle) {
      unmount(handle.instance)
    },
  }
}

/**
 * Turns each slot fragment into a Svelte snippet that adopts it.
 *
 * `createRawSnippet` is the seam for exactly this: `render` returns the markup for ONE container
 * element, and `setup` is handed that element afterwards — so the container arrives empty and Svelte
 * has nothing inside it to reconcile. These are live nodes already carrying Rask's handler ids and
 * DOM state, not markup to reparse.
 *
 * Svelte's default slot is the `children` prop, the same rename React needs.
 */
function adopt(slots: ExternalSlots): Record<string, unknown> {
  const out: Record<string, unknown> = {}

  for (const name of Object.keys(slots)) {
    const fragment = slots[name]
    const key = name === 'default' ? 'children' : name

    out[key] = createRawSnippet(() => ({
      render: () => `<div data-rask-slot="${escapeAttribute(name)}"></div>`,
      setup: (node: Element) => {
        // Adopt only into an empty container, so a re-entrant setup cannot append the same nodes twice.
        if (!node.firstChild) node.appendChild(fragment)
      },
    }))
  }

  return out
}

/**
 * Escapes a slot name for an HTML attribute.
 *
 * The name is author-controlled C# rather than user input, but `render` returns a STRING that Svelte
 * parses as markup — so a name carrying a quote would break out of the attribute and change the
 * shape of the container Rask is about to adopt into.
 */
function escapeAttribute(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}
