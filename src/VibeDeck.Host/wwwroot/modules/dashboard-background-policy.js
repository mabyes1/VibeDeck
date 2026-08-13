export function isFullscreenDisplayStreaming(activeMode, body) {
  return activeMode === "display" && Boolean(body?.classList?.contains("viewer-fullscreen"));
}

export function shouldRunDashboardBackgroundWork(activeMode, body, visibilityState = "visible") {
  return visibilityState !== "hidden" && !isFullscreenDisplayStreaming(activeMode, body);
}
