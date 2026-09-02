# Custom Deck contract

Custom Decks are ordinary HTML, CSS and JavaScript files discovered from:

```text
C:\ProgramData\VibeDeck\Decks\<deck-id>\
```

They do **not** need a build step. Add or replace the files, refresh Deck discovery, and the browser loads them directly.

## Static Deck isolation

A `static` Deck is rendered in an iframe with:

```html
sandbox="allow-scripts"
```

That sandbox intentionally gives the Deck an opaque origin. Deck authors must account for the following:

- the Deck is not a same-origin VibeDeck script even though its files are served by the Host;
- it cannot access the parent DOM;
- it cannot rely on VibeDeck's device-trust cookie, device token or authenticated API wrapper;
- a naked `fetch("/api/...")` to a protected Host endpoint may receive `401` or `403` on a paired phone;
- Web Storage such as `localStorage` / `sessionStorage` may be unavailable and may throw `SecurityError` because the iframe has an opaque origin; optional preferences must be wrapped in `try/catch` or use an in-memory fallback;
- secrets must never be placed in Deck files because those files are browser assets.

Do not add `allow-same-origin` to static Decks just to make a protected API call work. That weakens the isolation contract.

For example, a static Deck must not let an optional preference crash its startup path:

```js
function safeGetPreference(key, fallback = null) {
  try {
    return window.localStorage?.getItem(key) ?? fallback;
  } catch {
    return fallback;
  }
}
```

## Read-only Host data bridge

When a Host extension intentionally exposes data to Decks, its read endpoint belongs under:

```text
/api/deck-data/*
```

Static Decks request that data through the parent window. The parent performs the authenticated request and returns only JSON. Device tokens and cookies are never copied into the Deck iframe.

Request:

```js
const requestId = crypto.randomUUID();

window.parent.postMessage({
  type: "vibedeck:deck-request",
  requestId,
  action: "get-json",
  path: "/api/deck-data/example"
}, "*");
```

Response:

```js
window.addEventListener("message", event => {
  const message = event.data;
  if (message?.type !== "vibedeck:deck-response" || message.requestId !== requestId) return;

  if (message.ok) {
    console.log(message.data);
  } else {
    console.error(message.status, message.error);
  }
});
```

The bridge is deliberately narrow:

- only read-only JSON access is supported;
- the path must remain on the current VibeDeck Host;
- only `/api/deck-data/*` is accepted;
- arbitrary protected APIs and mutation endpoints are rejected;
- the iframe remains sandboxed and never receives authentication material.

If a Deck only needs public internet data, it may use normal browser networking subject to the remote server's CORS policy. The Host bridge is for explicit VibeDeck-provided Deck data, not a general-purpose proxy.

## Fullscreen ownership

VibeDeck owns the fullscreen/viewer shell. A Deck should size itself to its iframe viewport and must not assume it controls the top-level browser fullscreen state.

Fullscreen is intentionally chrome-free. VibeDeck does not place a floating exit button over Deck content. Users leave viewer mode with the browser/system Back action on mobile or `Escape` on a keyboard. Entering viewer mode pushes a temporary history sentinel, so Back exits viewer mode before it can navigate away from VibeDeck.

A Deck may also ask the parent to leave viewer mode:

```js
window.parent.postMessage({
  type: "vibedeck:deck-command",
  action: "exit-viewer"
}, "*");
```

Avoid calling `document.documentElement.requestFullscreen()` from inside a Deck unless the Deck is intentionally managing a nested fullscreen experience of its own.

## Minimal manifest

```json
{
  "name": "My Deck",
  "entry": "index.html",
  "icon": "✨"
}
```

See `src/VibeDeck.Host/DeckExamples/coding-pet/` for a static visual example. For event-style external integrations that push data into VibeDeck cards rather than building an entire Deck UI, use Custom Sources instead; that is a separate model documented in `docs/custom-data-sources-spec.md`.
