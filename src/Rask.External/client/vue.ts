// rask-external Vue adapter, vendored from Rask.External.
//
// A single-file component is compiled by @vitejs/plugin-vue, so nothing here integrates a compiler —
// this is the same three functions every other runtime implements.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createApp, h, reactive, type App, type Component } from 'vue'
import type { ExternalAdapter, ExternalProps } from './adapter'

/** What `mount` hands back: the app to unmount, and the reactive object updates are written into. */
interface VueHandle {
  app: App<Element>
  state: ExternalProps
}

/**
 * Wraps a Vue component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Vue component with no Rask import in it.
 */
export function vueComponent(component: Component): ExternalAdapter<VueHandle> {
  return {
    mount(element, props) {
      // Props live in ONE reactive object for the life of the island, and updates mutate it in place.
      // Re-creating it per update would give Vue a new object identity to diff against instead of a
      // tracked change, which reads as a remount to anything watching.
      const state = reactive({ ...props }) as ExternalProps

      // A wrapper whose render() READS the reactive object is what ties the two together. Spreading
      // it here is what makes the read happen during THIS render, so Vue tracks every prop and
      // re-renders the wrapper when one changes — which patches the real component's props. A
      // reconcile, never a remount.
      const app = createApp({
        render: () => h(component, { ...state }),
      })

      app.mount(element)
      return { app, state }
    },

    update(handle, props) {
      // Deleted first, then assigned. Vue reacts to a key being removed as well as to one changing,
      // and an unwired callback omits its key entirely — so without the delete a callback that was
      // cleared in C# would keep firing the stale one.
      for (const key of Object.keys(handle.state)) {
        if (!(key in props)) {
          delete handle.state[key]
        }
      }

      Object.assign(handle.state, props)
      return handle
    },

    unmount(handle) {
      handle.app.unmount()
    },
  }
}
