(() => {
  // ../Rask.Core/Resources/rask-sw-shared.ts
  self.addEventListener("push", (event) => {
    let data = {};
    try {
      data = event.data ? event.data.json() : {};
    } catch {
      data = { body: event.data ? event.data.text() : "" };
    }
    const title = data.title || "Notification";
    event.waitUntil(self.registration.showNotification(title, {
      body: data.body,
      icon: data.icon,
      badge: data.badge,
      tag: data.tag,
      data: data.data || {}
    }));
  });
  self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    const payload = event.notification.data;
    const url = (payload == null ? void 0 : payload.url) || "/";
    event.waitUntil(
      self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
        for (const client of clients) {
          if (client.url === url && "focus" in client) {
            return client.focus();
          }
        }
        return self.clients.openWindow ? self.clients.openWindow(url) : void 0;
      })
    );
  });

  // Resources/rask-sw.ts
  var RASK_CACHE = "rask-cache-v1";
  self.addEventListener("install", () => self.skipWaiting());
  self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));
  self.addEventListener("fetch", (event) => {
    const req = event.request;
    if (req.method !== "GET" || new URL(req.url).origin !== self.location.origin) {
      return;
    }
    event.respondWith((async () => {
      const cache = await caches.open(RASK_CACHE);
      try {
        const res = await fetch(req);
        if (res && res.ok) {
          cache.put(req, res.clone());
        }
        return res;
      } catch (err) {
        const cached = await cache.match(req);
        if (cached) {
          return cached;
        }
        if (req.mode === "navigate") {
          const shell = await cache.match("index.html") || await cache.match("./");
          if (shell) {
            return shell;
          }
        }
        throw err;
      }
    })());
  });
  var raskForwardSync = (event, kind) => event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        client.postMessage({ rask: kind, tag: event.tag });
      }
    })
  );
  self.addEventListener("sync", (event) => raskForwardSync(event, "sync"));
  self.addEventListener("periodicsync", (event) => raskForwardSync(event, "periodicsync"));
})();
