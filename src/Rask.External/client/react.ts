// rask-external React adapter, vendored from Rask.External.
//
// Covers Preact unchanged. create-vite's Preact template aliases `react` and `react-dom` to
// `preact/compat` in both tsconfig and the Vite plugin — the same aliasing the TypeScript SPA lane
// already relies on for TanStack Query — so this file type-checks and bundles against either, and
// Rask never needs to know which one it got.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createElement, type ComponentType } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import type { ExternalAdapter, ExternalProps } from './adapter'

/**
 * Wraps a React component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary React component with no Rask import in it.
 */
export function reactComponent<P extends object>(Component: ComponentType<P>): ExternalAdapter<Root> {
  const render = (root: Root, props: ExternalProps) =>
    root.render(createElement(Component, { ...props } as unknown as P))

  return {
    mount(element, props) {
      const root = createRoot(element)
      render(root, props)
      return root
    },

    // Re-rendering the root IS the update path — React diffs it against what it already rendered, so
    // this is a reconcile rather than a remount. It relies on the callbacks keeping their identity
    // between updates, which is exactly what the runtime's handler cache guarantees; without it every
    // update would look like new props to any memo or useEffect keyed on a callback.
    update(root, props) {
      render(root, props)
      return root
    },

    unmount(root) {
      root.unmount()
    },
  }
}
