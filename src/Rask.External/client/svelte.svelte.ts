// rask-external Svelte adapter, vendored from Rask.External.
//
// A `.svelte.ts` module rather than a plain `.ts` one, and that extension is load-bearing: `$state`
// is a COMPILER rune, not a function, so it does not exist in a file the Svelte plugin does not
// compile. Without it the only way to show new props would be to remount, which throws away the
// component's own state on every C# re-render — the one thing the diff boundary exists to prevent.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { mount, unmount, type Component } from 'svelte'
import type { ExternalAdapter, ExternalProps } from './adapter'

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
  return {
    mount(element, props) {
      // Handed to `mount` by reference and then mutated in place for the life of the island. Svelte
      // tracks the proxy's properties, so assigning to one is what reaches the component — replacing
      // this object instead would leave the component reading the original forever.
      const state: ExternalProps = $state({ ...props })

      const instance = mount(Component, { target: element, props: state }) as Record<string, unknown>
      return { instance, state }
    },

    update(handle, props) {
      // Deleted first, then assigned. An unwired callback omits its key entirely, so without the
      // delete a callback cleared in C# would keep firing the stale one.
      for (const key of Object.keys(handle.state)) {
        if (!(key in props)) {
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
