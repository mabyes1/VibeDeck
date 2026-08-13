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

// Prefer the low-latency path based on browser/runtime capability, not device
// class. Desktop browsers are at least as capable of receiving WebRTC H.264 as
// mobile browsers; an insecure non-loopback origin is the only common case
// where the browser path must stay on JPEG.
export function shouldPreferWebRtcDisplay({
  forced = false,
  hasPeerConnection = false,
  secureContext = false,
  loopback = false,
} = {}) {
  return Boolean(forced || (hasPeerConnection && (secureContext || loopback)));
}
