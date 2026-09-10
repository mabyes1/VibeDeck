export function buildKeepAwakeView({ hasWakeLock, videoPlaying, ios, protocol }) {
  if (hasWakeLock || videoPlaying) {
    return { text: "長亮：開", good: true };
  }
  if (ios && protocol !== "https:") {
    return { text: "長亮：需 HTTPS", good: false };
  }
  return { text: "長亮：等待互動", good: false };
}

export function createKeepAwakeController({
  document,
  window,
  navigator,
  video,
  isIos,
  isMobileClient,
  setWakeState,
}) {
  let wakeLock = null;
  let videoPlaying = false;
  let watchTimer = null;

  function updateCapability() {
    const view = buildKeepAwakeView({
      hasWakeLock: Boolean(wakeLock),
      videoPlaying,
      ios: isIos(),
      protocol: window.location.protocol,
    });
    setWakeState(view.text, view.good);
  }

  async function startVideo() {
    if (!video) return false;
    try {
      video.muted = true;
      video.defaultMuted = true;
      video.playsInline = true;
      video.setAttribute("playsinline", "");
      video.setAttribute("webkit-playsinline", "");
      video.loop = true;
      if (video.paused) await video.play();
      videoPlaying = !video.paused;
      return videoPlaying;
    } catch {
      videoPlaying = false;
      return false;
    }
  }

  function stopVideo() {
    videoPlaying = false;
    if (!video) return;
    try {
      video.pause();
      video.currentTime = 0;
    } catch { }
  }

  async function ensure() {
    if (document.visibilityState === "hidden") {
      updateCapability();
      return false;
    }

    let locked = false;
    if ("wakeLock" in navigator && window.isSecureContext) {
      try {
        if (!wakeLock) {
          wakeLock = await navigator.wakeLock.request("screen");
          wakeLock.addEventListener("release", () => {
            wakeLock = null;
            if (document.visibilityState === "visible") {
              startVideo().finally(updateCapability);
            } else {
              updateCapability();
            }
          });
        }
        locked = Boolean(wakeLock);
      } catch {
        wakeLock = null;
      }
    }

    if (!locked && (isIos() || isMobileClient() || !window.isSecureContext)) {
      locked = await startVideo();
    }
    updateCapability();
    return locked;
  }

  async function release() {
    try {
      await wakeLock?.release();
    } catch { }
    wakeLock = null;
    stopVideo();
    updateCapability();
  }

  function isDesired() {
    return true;
  }

  function startWatch() {
    if (watchTimer) return;
    watchTimer = setInterval(() => {
      if (document.visibilityState !== "visible" || wakeLock) return;
      if (video && !video.paused) {
        videoPlaying = true;
        return;
      }
      ensure();
    }, 15000);
  }

  async function handleVisibilityChange() {
    if (document.visibilityState === "visible") {
      await ensure();
      return true;
    }
    await release();
    return false;
  }

  function handlePointerDown() {
    if ((isIos() || isMobileClient()) && document.visibilityState !== "hidden") ensure();
  }

  return {
    updateCapability,
    ensure,
    release,
    isDesired,
    startWatch,
    handleVisibilityChange,
    handlePointerDown,
  };
}
