// Environment detection pure helpers, extracted verbatim from index.js.
// These have zero app-state dependencies (only read location/navigator).
// index.js wraps some of these with device-preview overrides; these are
// the underlying detection primitives.

export function isLoopbackHost() {
  const host = location.hostname;
  return host === "localhost" || host === "127.0.0.1" || host === "[::1]";
}

export function isIosUA() {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) ||
    (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}

export function isIphoneUA() {
  return /iPhone|iPod/.test(navigator.userAgent || "");
}

export function isMobileUA() {
  return isIosUA() || /Android|Mobile|webOS|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent || "");
}
