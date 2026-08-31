// rask-external Lit adapter, vendored from Rask.External.
//
// The cheapest runtime of the set, and the only one that imports nothing: a Lit component IS a custom
// element, so mounting is createElement plus property assignment, updating is the same assignment,
// and unmounting is remove(). Lit's reactive properties re-render on assignment with nothing in
// between — there is no reconciler to drive.
//
// This works for any custom element with property-shaped inputs, not only Lit ones.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import type { ExternalAdapter, ExternalProps, ExternalSlots } from './adapter'

/**
 * Wraps a custom element as an island adapter.
 *
 * @param tag The registered element name, e.g. `'app-gauge'`. Importing the module that defines it is
 *   the build's job — the generated entry imports the component for its side effect and calls this.
 */
export function litComponent(tag: string): ExternalAdapter<HTMLElement> {
  return {
    mount(element, props, slots) {
      const node = document.createElement(tag)
      assign(node, props)

      // The one runtime where slots need no adoption trick: a custom element projects its LIGHT DOM
      // children through <slot>, so appending them is all there is to it. The nodes stay ordinary
      // children of the element — nothing clones them, nothing re-renders them.
      for (const name of Object.keys(slots ?? {})) {
        const fragment = (slots as ExternalSlots)[name]
        if (name !== 'default') {
          // Named projection is an attribute on each child, which is how <slot name="..."> matches.
          for (const child of [...fragment.children]) child.setAttribute('slot', name)
        }
        node.appendChild(fragment)
      }

      element.appendChild(node)
      return node
    },

    update(node, props) {
      assign(node, props)
      return node
    },

    unmount(node) {
      node.remove()
    },
  }
}

/**
 * Assigns props as PROPERTIES, never attributes.
 *
 * An attribute would stringify everything — an array of points would arrive as "[object Object]" —
 * and Lit only reflects the direction it was asked to. Properties also carry the revived callbacks,
 * which cannot survive an attribute at all.
 */
function assign(node: HTMLElement, props: ExternalProps): void {
  for (const key of Object.keys(props)) {
    ;(node as unknown as Record<string, unknown>)[key] = props[key]
  }
}
