// Web Push against this app's ASP.NET host. The host owns the VAPID key pair; the browser only
// ever sees the public half.
//
// The browser half lives in Rask's shared browser layer — the same modules Rask's own clients
// run — so the base64url VAPID key and the nested-vs-flat subscription shape are handled there.
// What is left here is yours: which endpoints, and when.

import {
  getSubscription,
  isSupported,
  requestPermission,
  subscribe,
  unsubscribe,
} from './rask/browser/webPush'
import type { PushSubscriptionInfo } from './rask/browser/webPush'

export type { PushSubscriptionInfo }

/** Whether this browser can subscribe at all. False on http:// and in older browsers. */
export function pushSupported(): boolean {
  return isSupported()
}

/**
 * Subscribes this browser and registers it with the host.
 *
 * Returns null when push is unsupported, when the host has no VAPID key configured yet, or when
 * the user denies permission — three ordinary outcomes, none of them an error to throw over.
 */
export async function subscribeToPush(): Promise<PushSubscriptionInfo | null> {
  if (!isSupported()) return null

  const response = await fetch('/_push/key')
  if (!response.ok) return null
  const { publicKey } = (await response.json()) as { publicKey: string }

  // Empty until you configure a key pair. Asking the browser to subscribe with an empty
  // applicationServerKey throws, so this stops here instead.
  if (!publicKey) return null

  if ((await requestPermission()) !== 'granted') return null

  const info = await subscribe(publicKey)
  await fetch('/_push/subscribe', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(info),
  })

  return info
}

/** Unsubscribes this browser and tells the host to forget it. Safe to call when not subscribed. */
export async function unsubscribeFromPush(): Promise<void> {
  if (!isSupported()) return

  const info = await getSubscription()
  if (!info) return

  // The host is told BEFORE the browser drops it: unsubscribe() invalidates the endpoint, and a
  // failure after that point would leave the host sending to a subscription that can never work.
  await fetch('/_push/unsubscribe', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(info),
  })

  await unsubscribe()
}
