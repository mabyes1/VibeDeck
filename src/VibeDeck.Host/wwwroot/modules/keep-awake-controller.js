export function buildKeepAwakeView({ desired, hasWakeLock, videoPlaying, ios, protocol }) {
  if (!desired) {
    return { text: "長亮：關", good: false, buttonText: "長亮 OFF", active: false };
  }
  if (hasWakeLock || videoPlaying) {
    return { text: "長亮：開", good: true, buttonText: "長亮 ON", active: true };
  }
  if (ios && protocol !== "https:") {
    return { text: "長亮：需 HTTPS", good: false, buttonText: "長亮 ON", active: false };
  }
  return { text: "長亮：點按鈕開啟", good: false, buttonText: "長亮 ON", active: false };
}

export function createKeepAwakeController({
  document,
  window,
  navigator,
  localStorage,
  button,
  video,
  isIos,
  isMobileClient,
  setWakeState,
}) {
  let wakeLock = null;
  let desired = localStorage.getItem("vibeDeckKeepAwake") !== "0";
  let videoPlaying = false;
  let watchTimer = null;

  function updateCapability() {
    const view = buildKeepAwakeView({
      desired,
      hasWakeLock: Boolean(wakeLock),
      videoPlaying,
      ios: isIos(),
      protocol: window.location.protocol,
    });
    setWakeState(view.text, view.good);
    if (button) {
      button.textContent = view.buttonText;
      button.classList.toggle("active", view.active);
    }
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
    if (!desired || document.visibilityState === "hidden") {
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
            if (desired && document.visibilityState === "visible") {
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

  async function setDesired(enabled) {
    desired = Boolean(enabled);
    localStorage.setItem("vibeDeckKeepAwake", desired ? "1" : "0");
    if (!desired) {
      await release();
      return;
    }
    await ensure();
  }

  function isDesired() {
    return desired;
  }

  function startWatch() {
    if (watchTimer) return;
    watchTimer = setInterval(() => {
      if (!desired || document.visibilityState !== "visible" || wakeLock) return;
      if (video && !video.paused) {
        videoPlaying = true;
        return;
      }
      ensure();
    }, 15000);
  }

  async function handleVisibilityChange() {
    if (document.visibilityState === "visible") {
      if (desired) await ensure();
      return true;
    }
    await release();
    return false;
  }

  function handlePointerDown() {
    if (desired && (isIos() || isMobileClient())) ensure();
  }

  async function toggle() {
    await setDesired(!desired);
  }

  return {
    updateCapability,
    ensure,
    release,
    setDesired,
    isDesired,
    startWatch,
    handleVisibilityChange,
    handlePointerDown,
    toggle,
  };
}
