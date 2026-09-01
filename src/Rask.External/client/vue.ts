// rask-external Vue adapter, vendored from Rask.External.
//
// A single-file component is compiled by @vitejs/plugin-vue, so nothing here integrates a compiler —
// this is the same three functions every other runtime implements.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createApp, h, reactive, type App, type Component, type VNode } from 'vue'
import type { ExternalAdapter, ExternalProps, ExternalSlots } from './adapter'

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
    mount(element, props, slots) {
      // Props live in ONE reactive object for the life of the island, and updates mutate it in place.
      // Re-creating it per update would give Vue a new object identity to diff against instead of a
      // tracked change, which reads as a remount to anything watching.
      const state = reactive({ ...props }) as ExternalProps

      // Created once, for the same reason the React adapter memoises its slot elements: a fresh slot
      // function per render returns fresh vnodes, and Vue would patch the container — discarding the
      // Rask-owned nodes adopted into it.
      const slotFns = adopt(slots ?? {})

      // A wrapper whose render() READS the reactive object is what ties the two together. Vue tracks
      // the property reads during render and re-renders this wrapper when they change, which patches
      // the real component's props — a reconcile, never a remount.
      const app = createApp({
        render: () => h(component, { ...state }, slotFns),
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

/**
 * Turns each slot fragment into a Vue slot function that adopts it.
 *
 * The container renders EMPTY and is filled through a ref, exactly as in the React adapter: Vue has
 * no children to patch inside it, so it never touches the nodes Rask put there. These are live nodes
 * already carrying Rask's handler ids and DOM state, not markup to reparse.
 *
 * Vue's default slot is called `default`, which is what `<slot />` renders — so unlike React, where
 * it has to become `children`, the name Rask uses already matches.
 */
function adopt(slots: ExternalSlots): Record<string, () => VNode> {
  const out: Record<string, () => VNode> = {}

  for (const name of Object.keys(slots)) {
    const fragment = slots[name]

    // The vnode is built ONCE and returned by every call, so Vue sees the same element identity each
    // render and patches nothing.
    const vnode = h('div', {
      'data-rask-slot': name,
      ref: (node: unknown) => {
        // Vue calls a ref with null on unmount. Adopt only into an empty container, or a re-entrant
        // call appends the same nodes twice.
        const el = node as HTMLElement | null
        if (el && !el.firstChild) el.appendChild(fragment)
      },
    })

    out[name] = () => vnode
  }

  return out
}
