// A plain custom element, used as a Lit-runtime island: no npm import, no decorators (Vite's transform does not lower
// them, and an element that never upgrades renders empty with nothing said).
//
// The adapter assigns each prop as a property while the island mounts, inside the try that reports a mount failure to
// the devtools. So `broken = true` throwing from its setter is a real island mount failure, not a page error.
import type { DevToolsMeterProps } from '@rask/DevToolsMeter.props'

class DevToolsMeterElement extends HTMLElement {
  #label: DevToolsMeterProps['label'] = ''
  #value: DevToolsMeterProps['value'] = 0

  set label(value: string) {
    this.#label = value
    this.#render()
  }

  set value(value: number) {
    this.#value = value
    this.#render()
  }

  set broken(value: boolean) {
    if (value) {
      throw new Error('the meter island failed on purpose')
    }
  }

  connectedCallback(): void {
    this.#render()
  }

  #render(): void {
    this.textContent = `${this.#label}: ${this.#value}`
  }
}

customElements.define('devtools-e2e-meter', DevToolsMeterElement)

export default 'devtools-e2e-meter'
