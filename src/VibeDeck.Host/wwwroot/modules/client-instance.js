// Client instance ID persistence, extracted verbatim from index.js.
// Depends on readCookie/writeCookie from device-credentials.js.

import { readCookie, writeCookie } from "./device-credentials.js?v=1";

export const CLIENT_INSTANCE_KEY = "vibeDeckClientInstanceId";
export const CLIENT_INSTANCE_COOKIE = "VibeDeck-Client-Instance";

export function getOrCreateClientInstanceId() {
  let value = "";
  try {
    value = localStorage.getItem(CLIENT_INSTANCE_KEY)
      || readCookie(CLIENT_INSTANCE_COOKIE)
      || "";
  } catch { }
  if (!value) {
    value = crypto.randomUUID?.() || Array.from(crypto.getRandomValues(new Uint8Array(18)))
      .map(item => item.toString(16).padStart(2, "0")).join("");
  }
  try {
    localStorage.setItem(CLIENT_INSTANCE_KEY, value);
  } catch { }
  writeCookie(CLIENT_INSTANCE_COOKIE, value, 800);
  return value;
}
