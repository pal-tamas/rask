// The front-end half of the `Dial : LitComponent` declared in ExternalRenderTests, and the reason
// this file exists at all: it makes the build's prop type-check RUN.
//
// Without a real pair somewhere, _RaskExternalTypeCheck is wired and never exercised — the wiring
// would pass review, ship, and be discovered broken by whoever first relied on it. Here the guarantee
// is load-bearing on every build of this project: rename `Value` on Dial in C# and this stops
// compiling, which fails the build that generated the type.
//
// Deliberately imports nothing from npm. That is the case the measurement turned up — a plain custom
// element type-checks against its generated props with no node_modules at all — and it is what lets
// this run in a test project that has no package.json.

import type { DialProps } from '@rask/Dial.props'

class RaskDial extends HTMLElement {
    props?: DialProps

    connectedCallback(): void {
        // Reads the prop the C# declares. `value` is a double there, so a number here.
        const value = this.props?.value ?? 0;
        this.setAttribute('aria-valuenow', String(value));
        this.textContent = `${Math.round(value * 100)}%`;
    }
}

customElements.define('rask-dial', RaskDial);

// A Lit-runtime module default-exports its registered tag name: a custom element registers its own
// tag and nothing else about the file reveals it.
export default 'rask-dial';
