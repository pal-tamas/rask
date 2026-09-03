// rask-external Preact adapter, vendored from Rask.External.
//
// Preact directly, not through preact/compat. The React adapter still serves an app that aliases
// react to preact/compat, and that route stays supported — but it only works while the aliasing is in
// place, and it makes every error message name a framework the project does not use. This one imports
// what the island imports.
//
// Rendering into the SAME element on every update is the whole update path: Preact diffs against the
// tree it already put there, so this reconciles rather than remounts. `render` is idempotent that way
// by design — there is no root object to keep, which is why the handle is the element itself.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { h, render, type ComponentType } from 'preact'
import type { ExternalAdapter, ExternalProps } from './adapter'

/**
 * Wraps a Preact component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary Preact component with no Rask import in it.
 */
export function preactComponent<P extends object>(Component: ComponentType<P>): ExternalAdapter<Element> {
  const draw = (element: Element, props: ExternalProps) =>
    render(h(Component as ComponentType<ExternalProps>, props), element)

  return {
    mount(element, props) {
      draw(element, props)
      return element
    },

    // Re-rendering into the same element IS the reconcile. It relies on the callbacks keeping their
    // identity between updates, which the runtime's handler cache guarantees; without it every update
    // would look like new props to anything keyed on a callback.
    update(element, props) {
      draw(element, props)
      return element
    },

    // `render(null, element)` is Preact's unmount: it runs the tree's cleanup effects and empties the
    // element. Simply dropping the element would leak every effect still subscribed inside it.
    unmount(element) {
      render(null, element)
    },
  }
}
