import { afterEach, describe, expect, it, vi } from 'vite-plus/test'
import { pushSupported, subscribeToPush, unsubscribeFromPush } from './push'

// `vp test` runs in Node, which has a navigator but no service worker: exactly a browser that cannot
// subscribe, so every entry point has to answer without asking the host for anything.
describe('push', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('is unsupported without a service worker', () => {
    expect(pushSupported()).toBe(false)
  })

  it('neither subscribes nor calls the host when unsupported', async () => {
    const fetch = vi.spyOn(globalThis, 'fetch')

    await expect(subscribeToPush()).resolves.toBeNull()
    await unsubscribeFromPush()

    expect(fetch).not.toHaveBeenCalled()
  })
})
