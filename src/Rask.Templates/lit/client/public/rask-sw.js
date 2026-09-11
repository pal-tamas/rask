// Registered from index.html. Handles Web Push delivered by the ASP.NET host (Rask.WebPush);
// the payload shape is what WebPushMessage serializes.

self.addEventListener('push', (event) => {
  // No initialiser and no catch binding: both branches below assign, and the error itself is not
  // used — a payload that is not JSON is delivered as text rather than reported.
  let data
  try {
    data = event.data ? event.data.json() : {}
  } catch {
    data = { body: event.data ? event.data.text() : '' }
  }
  const title = data.title || 'Notification'
  event.waitUntil(
    self.registration.showNotification(title, {
      body: data.body,
      icon: data.icon,
      badge: data.badge,
      tag: data.tag,
      data: data.data || {},
    }),
  )
})

// Focus an already-open window for the target URL rather than opening a second one.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = (event.notification.data && event.notification.data.url) || '/'
  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if (client.url === url && 'focus' in client) {
          return client.focus()
        }
      }
      return self.clients.openWindow ? self.clients.openWindow(url) : undefined
    }),
  )
})
