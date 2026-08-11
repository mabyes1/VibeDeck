// Device credential + cookie storage primitives, extracted verbatim from
// index.js. These are pure helpers (no shared app state) — index.js imports
// them and its call sites stay unchanged. The stateful persistDeviceCredentials,
// which mutates the live deviceToken/deviceId, stays in index.js and calls these.

export const DEVICE_TOKEN_KEY = "vibeDeckDeviceToken";
export const DEVICE_ID_KEY = "vibeDeckDeviceId";
export const DEVICE_COOKIE = "VibeDeck-Device-Token";
export const DEVICE_TOKEN_HISTORY_KEY = "vibeDeckDeviceTokenHistory.v1";
export const DEVICE_TOKEN_HISTORY_LIMIT = 4;

export function readCookie(name) {
  const parts = (`; ${document.cookie || ""}`).split(`; ${name}=`);
  if (parts.length < 2) return "";
  return decodeURIComponent(parts.pop().split(";").shift() || "");
}

export function writeCookie(name, value, days) {
  const maxAge = Math.max(0, Math.floor(days * 24 * 60 * 60));
  const secure = location.protocol === "https:" ? "; Secure" : "";
  document.cookie = `${name}=${encodeURIComponent(value || "")}; Path=/; Max-Age=${maxAge}; SameSite=Lax${secure}`;
}

export function writeDeviceCookies(token, days) {
  writeCookie(DEVICE_COOKIE, token, days);
}

export function loadStoredDeviceCredentials() {
  const token = localStorage.getItem(DEVICE_TOKEN_KEY)
    || sessionStorage.getItem(DEVICE_TOKEN_KEY)
    || readCookie(DEVICE_COOKIE)
    || "";
  const id = localStorage.getItem(DEVICE_ID_KEY)
    || sessionStorage.getItem(DEVICE_ID_KEY)
    || "";
  return { token, id };
}

export function loadDeviceTokenHistory() {
  try {
    const values = JSON.parse(localStorage.getItem(DEVICE_TOKEN_HISTORY_KEY) || "[]");
    return Array.isArray(values)
      ? values.map(value => String(value || "").trim()).filter(Boolean).slice(0, DEVICE_TOKEN_HISTORY_LIMIT)
      : [];
  } catch {
    return [];
  }
}

export function saveDeviceTokenHistory(values) {
  try {
    const unique = [...new Set(values.map(value => String(value || "").trim()).filter(Boolean))]
      .slice(0, DEVICE_TOKEN_HISTORY_LIMIT);
    if (unique.length) localStorage.setItem(DEVICE_TOKEN_HISTORY_KEY, JSON.stringify(unique));
    else localStorage.removeItem(DEVICE_TOKEN_HISTORY_KEY);
  } catch {
    // Token history is only a recovery path; normal cookie storage still works.
  }
}
