export const AUTO_WEBRTC_COOLDOWN_MS = 120000;
export const PREFER_WEBRTC_COOLDOWN_MS = 30000;

export function webRtcCooldownMs(transportMode) {
  return transportMode === "webrtc"
    ? PREFER_WEBRTC_COOLDOWN_MS
    : AUTO_WEBRTC_COOLDOWN_MS;
}

/**
 * Owns the single cooldown retry timer. The controller supplies its current
 * generation and transport preference so stale callbacks cannot reconnect a
 * stopped page or override an explicit JPEG selection.
 */
export function createWebRtcRetryScheduler({
  getGeneration,
  getTransportMode,
  prefersWebRtcDisplay,
  retry,
  now = () => Date.now(),
  setTimer = (callback, delay) => setTimeout(callback, delay),
  clearTimer = timer => clearTimeout(timer),
}) {
  let timer = null;
  let revision = 0;

  function canRetry(generation) {
    if (generation !== getGeneration()) return false;
    const mode = getTransportMode();
    if (mode === "jpeg") return false;
    return mode === "webrtc" || Boolean(prefersWebRtcDisplay());
  }

  function cancel() {
    revision += 1;
    if (timer !== null) {
      clearTimer(timer);
      timer = null;
    }
  }

  function schedule(generation, cooldownUntil) {
    cancel();
    if (!canRetry(generation)) return false;

    const parsedDeadline = Number(cooldownUntil);
    const deadline = Number.isFinite(parsedDeadline) ? parsedDeadline : now();
    const scheduledRevision = revision;

    const arm = () => {
      const delay = Math.max(0, deadline - now());
      timer = setTimer(() => {
        timer = null;
        if (scheduledRevision !== revision || !canRetry(generation)) return;

        // Timers may fire early after sleep/clock adjustments. Preserve the
        // original deadline instead of creating an early retry burst.
        if (now() < deadline) {
          arm();
          return;
        }

        revision += 1;
        retry();
      }, delay);
    };

    arm();
    return true;
  }

  return {
    schedule,
    cancel,
    hasScheduledRetry: () => timer !== null,
  };
}
