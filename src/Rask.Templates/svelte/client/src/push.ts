// Web Push against this app's ASP.NET host, over the browser's own API. The host owns the VAPID
// key pair; the browser only ever sees the public half.

/** Whether this browser can subscribe at all. False on http:// and in older browsers. */
export function pushSupported(): boolean {
  return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window
}

/**
 * Subscribes this browser and registers it with the host.
 *
 * Returns null when push is unsupported, when the host has no VAPID key configured yet, or when
 * the user denies permission — three ordinary outcomes, none of them an error to throw over.
 */
export async function subscribeToPush(): Promise<PushSubscription | null> {
  if (!pushSupported()) return null

  const response = await fetch('/_rask/push/key')
  if (!response.ok) return null
  const { publicKey } = (await response.json()) as { publicKey: string }

  // Empty until you configure a key pair. Asking the browser to subscribe with an empty
  // applicationServerKey throws, so this stops here instead.
  if (!publicKey) return null

  if ((await Notification.requestPermission()) !== 'granted') return null

  const registration = await navigator.serviceWorker.ready
  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: toBytes(publicKey),
  })

  // toJSON() is { endpoint, expirationTime, keys: { p256dh, auth } }, which is what the host reads.
  await post('/_rask/push/subscribe', subscription.toJSON())

  return subscription
}

/** Unsubscribes this browser and tells the host to forget it. Safe to call when not subscribed. */
export async function unsubscribeFromPush(): Promise<void> {
  if (!pushSupported()) return

  const registration = await navigator.serviceWorker.ready
  const subscription = await registration.pushManager.getSubscription()
  if (!subscription) return

  // The host is told BEFORE the browser drops it: unsubscribe() invalidates the endpoint, and a
  // failure after that point would leave the host sending to a subscription that can never work.
  await post('/_rask/push/unsubscribe', { endpoint: subscription.endpoint })

  await subscription.unsubscribe()
}

function post(url: string, body: unknown): Promise<Response> {
  return fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
}

/** The host answers the VAPID key as base64url; the browser wants its bytes. */
function toBytes(base64Url: string): Uint8Array<ArrayBuffer> {
  const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/')
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')
  const raw = atob(padded)
  const bytes = new Uint8Array(raw.length)
  for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i)
  return bytes
}
