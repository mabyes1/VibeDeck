// E-ink hardware detection primitives, extracted verbatim from index.js.
// These are pure helpers (no app-state dependency). Functions that depend on
// isDevicePreview (isEinkClient, writeEinkPreference, ensureEinkPreferenceSticky)
// stay in index.js and call these primitives.

export const EINK_PREF_KEY = "vibeDeckEink";

export function readEinkQuery() {
  const requested = new URLSearchParams(location.search).get("eink");
  if (requested === "1" || requested === "true") return true;
  if (requested === "0" || requested === "false") return false;
  return null;
}

export function readEinkCookie() {
  try {
    const match = document.cookie.match(/(?:^|;\s*)vibeDeckEink=([01])/);
    return match ? match[1] : null;
  } catch {
    return null;
  }
}

export function looksLikeBooxScreen() {
  try {
    const widths = [
      screen.width || 0,
      screen.height || 0,
      window.innerWidth || 0,
      window.innerHeight || 0,
      document.documentElement?.clientWidth || 0,
      document.documentElement?.clientHeight || 0
    ];
    const w = Math.max(...widths);
    const h = Math.min(...widths.filter(v => v > 0));
    // BOOX Go Color 7: 1680 × 1264 (allow CSS-pixel / density variance)
    if (w >= 1180 && w <= 1900 && h >= 980 && h <= 1500 && (w / Math.max(h, 1)) >= 1.15) {
      return true;
    }
  } catch {
    // ignore
  }
  return false;
}

export function detectEinkHardware() {
  const ua = navigator.userAgent || "";
  if (/VibeDeck-EInk|BOOX|ONYX|Onyx|eInk|E-Ink|E Ink/i.test(ua)) return true;
  // Generic Android Chrome on a known BOOX panel still wants the paper layout.
  if (/Android/i.test(ua) && looksLikeBooxScreen()) return true;
  return false;
}
