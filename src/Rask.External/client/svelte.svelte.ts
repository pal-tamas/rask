// rask-external Svelte adapter, vendored from Rask.External.
//
// A `.svelte.ts` module rather than a plain `.ts` one, and that extension is load-bearing: `$state`
// is a COMPILER rune, not a function, so it does not exist in a file the Svelte plugin does not
// compile. Without it the only way to show new props would be to remount, which throws away the
// component's own state on every C# re-render — the one thing the diff boundary exists to prevent.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { mount, unmount, type Component } from 'svelte'
import type { ExternalAdapter, ExternalNode, ExternalProps } from './adapter'
import RaskHost from './RaskHost.svelte'

/**
 * What the vendored host component renders: the island's component, its props, and its children.
 *
 * Runes on class fields, so each is tracked on its own. The props are one `$state` object mutated in place,
 * as before; the children are `$state.raw`, replaced whole on every update, because C# sends the whole tree
 * and deep-proxying it would only cost a proxy per node for changes nothing tracks at that grain.
 */
class SvelteHost {
  readonly component: Component<Record<string, unknown>>
  props: ExternalProps = $state({})
  children: readonly ExternalNode[] | null = $state.raw(null)

  constructor(component: Component<Record<string, unknown>>) {
    this.component = component
  }
}

/** What `mount` hands back: the host instance to unmount, and the state updates are written into. */
interface SvelteHandle {
  instance: Record<string, unknown>
  host: SvelteHost
}

/**
 * Wraps a Svelte component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Svelte component with no Rask import in it.
 */
export function svelteComponent(Component: Component<Record<string, unknown>>): ExternalAdapter<SvelteHandle> {
  return {
    mount(element, props, children) {
      const host = new SvelteHost(Component)
      Object.assign(host.props, props)
      host.children = children ?? null

      // Mounted through a vendored host component rather than directly: `createRawSnippet` renders HTML
      // strings only, so a child COMPONENT needs real Svelte markup to become a `children` snippet. An
      // island given no children renders exactly as a direct mount would.
      const instance = mount(RaskHost, { target: element, props: { host } }) as Record<string, unknown>
      return { instance, host }
    },

    update(handle, props, children) {
      // Deleted first, then assigned. An unwired callback omits its key entirely, so without the
      // delete a callback cleared in C# would keep firing the stale one.
      for (const key of Object.keys(handle.host.props)) {
        if (!(key in props)) {
          delete handle.host.props[key]
        }
      }

      Object.assign(handle.host.props, props)
      handle.host.children = children ?? null
      return handle
    },

    unmount(handle) {
      unmount(handle.instance)
    },
  }
}
