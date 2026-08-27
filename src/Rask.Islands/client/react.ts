// rask-islands React adapter, vendored from Rask.Islands.
//
// Covers Preact unchanged. create-vite's Preact template aliases `react` and `react-dom` to
// `preact/compat` in both tsconfig and the Vite plugin — the same aliasing the TypeScript SPA lane
// already relies on for TanStack Query — so this file type-checks and bundles against either, and
// Rask never needs to know which one it got.
//
// You own this file. It is refreshed on build only while the header line above is intact.

import { createElement, type ComponentType, type ReactElement } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import type { IslandAdapter, IslandProps, IslandSlots } from './adapter'

/**
 * Wraps a React component as an island adapter.
 *
 * The build generates one entry per island that calls this and default-exports the result, so the
 * component itself stays an ordinary React component with no Rask import in it.
 */
export function reactIsland<P extends object>(Component: ComponentType<P>): IslandAdapter<Root> {
  // Created ONCE at mount and reused by every update. React compares children by identity, so
  // handing back the same element objects is what stops it re-rendering the slot containers — and a
  // re-render would discard the Rask-owned nodes adopted into them.
  let slotElements: Record<string, ReactElement> = {}

  const render = (root: Root, props: IslandProps) =>
    root.render(createElement(Component, { ...props, ...slotElements } as unknown as P))

  return {
    mount(element, props, slots) {
      slotElements = adopt(slots ?? {})
      const root = createRoot(element)
      render(root, props)
      return root
    },

    // Re-rendering the root IS the update path — React diffs it against what it already rendered, so
    // this is a reconcile rather than a remount. It relies on the callbacks keeping their identity
    // between updates, which is exactly what the runtime's handler cache guarantees; without it every
    // update would look like new props to any memo or useEffect keyed on a callback.
    //
    // The slot elements are spread in here too. Dropping them would delete the containers on the
    // first prop change and take Rask's adopted nodes with them.
    update(root, props) {
      render(root, props)
      return root
    },

    unmount(root) {
      root.unmount()
    },
  }
}

/**
 * Turns each slot fragment into a React element that adopts it.
 *
 * The container renders EMPTY and is filled from a ref — that is the whole trick. React has no
 * children to reconcile inside it, so it never touches the nodes Rask put there. Note this is not
 * `dangerouslySetInnerHTML`: these are live nodes that already carry Rask's handler ids and DOM
 * state, not markup to reparse.
 *
 * The default slot arrives as `children`, so `<Panel>{...}</Panel>` reads naturally in the component;
 * a named slot arrives under its own prop name.
 */
function adopt(slots: IslandSlots): Record<string, ReactElement> {
  const out: Record<string, ReactElement> = {}

  for (const name of Object.keys(slots)) {
    const fragment = slots[name]

    out[name === 'default' ? 'children' : name] = createElement('div', {
      'data-rask-slot': name,
      // The container's contents legitimately differ from anything React rendered. That mismatch is
      // the design, so silence the warning rather than leave a false alarm in every console.
      suppressHydrationWarning: true,
      ref: (node: HTMLElement | null) => {
        // React calls a ref with null on unmount and again with the node after. Adopt only into an
        // empty container, or a re-entrant call appends the same nodes twice.
        if (node && !node.firstChild) node.appendChild(fragment)
      },
    })
  }

  return out
}
