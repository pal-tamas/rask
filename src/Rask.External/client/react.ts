// rask-external React adapter, vendored from Rask.External.
//
// Covers Preact unchanged. create-vite's Preact template aliases `react` and `react-dom` to
// `preact/compat` in both tsconfig and the Vite plugin, so this file type-checks and bundles against
// either, and Rask never needs to know which one it got.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createElement, type ComponentType, type ReactNode } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import type { ExternalAdapter, ExternalNode, ExternalProps } from './adapter'

/**
 * Wraps a React component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary React component with no Rask import in it.
 */
export function reactComponent<P extends object>(Component: ComponentType<P>): ExternalAdapter<Root> {
  const render = (root: Root, props: ExternalProps, children?: readonly ExternalNode[]) =>
    root.render(createElement(Component, props as unknown as P, ...nodes(children)))

  return {
    mount(element, props, children) {
      const root = createRoot(element)
      render(root, props, children)
      return root
    },

    // Re-rendering the root IS the update path — React diffs it against what it already rendered, so
    // this is a reconcile rather than a remount, children included: a child island keeps its state
    // across a C# re-render because it is the same component at the same key. It relies on the
    // callbacks keeping their identity between updates, which is exactly what the runtime's handler
    // cache guarantees; without it every update would look like new props to any memo or useEffect
    // keyed on a callback.
    update(root, props, children) {
      render(root, props, children)
      return root
    },

    unmount(root) {
      root.unmount()
    },
  }
}

/**
 * The children C# gave the island, as React nodes: text as text, a child island as its own component
 * with its own props and children — all inside this one root, so the whole tree reconciles together.
 *
 * Returned for a rest argument, so a single child is `props.children` itself rather than a one-element
 * array, exactly as JSX passes it.
 */
function nodes(children: readonly ExternalNode[] | undefined | null): ReactNode[] {
  if (!children) {
    return []
  }

  return children.map((child) =>
    typeof child === 'string'
      ? child
      : createElement(
          child.component as ComponentType<Record<string, unknown>>,
          child.key === null ? child.props : { ...child.props, key: child.key },
          ...nodes(child.children),
        ),
  )
}
