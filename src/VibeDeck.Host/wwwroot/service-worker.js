// Service workers were retired in v47. VibeDeck needs a live PC Host and must
// never substitute cached HTML for an API response.
self.addEventListener("install", event => {
  event.waitUntil(self.skipWaiting());
});

self.addEventListener("activate", event => {
  event.waitUntil((async () => {
    const keys = await caches.keys();
    await Promise.all(keys
      .filter(key => key.startsWith("vibedeck-"))
      .map(key => caches.delete(key)));
    await self.registration.unregister();
    const clients = await self.clients.matchAll({ type: "window" });
    await Promise.all(clients.map(client => client.navigate(client.url)));
  })());
});
